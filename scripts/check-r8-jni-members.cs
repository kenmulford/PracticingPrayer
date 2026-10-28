// Fails when a [Register]-bound member of a .NET Java binding is missing from the shipped DEX and from android.jar: R8 cannot see JNI reads, so it may strip them.
// Usage: dotnet run scripts/check-r8-jni-members.cs -- <linkedAssembliesDir> <aabPath> [--dexdump <path>] [--android-jar <path>]
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

string? dexdump = null, androidJar = null;
var pos = new List<string>();
for (var i = 0; i < args.Length; i++) {
  if (args[i] == "--dexdump" && i + 1 < args.Length) dexdump = args[++i];
  else if (args[i] == "--android-jar" && i + 1 < args.Length) androidJar = args[++i];
  else pos.Add(args[i]);
}
if (pos.Count != 2) { Console.Error.WriteLine("usage: check-r8-jni-members.cs <linkedAssembliesDir> <aabPath> [--dexdump <path>] [--android-jar <path>]"); return 2; }
var (asmDir, aabPath) = (pos[0], pos[1]);

dexdump ??= FindDexdump();
if (dexdump == null) { Console.Error.WriteLine("dexdump not found: set ANDROID_HOME/ANDROID_SDK_ROOT or pass --dexdump"); return 2; }
androidJar ??= FindAndroidJar();
if (androidJar == null || !File.Exists(androidJar)) { Console.Error.WriteLine("android.jar not found: set ANDROID_HOME/ANDROID_SDK_ROOT or pass --android-jar"); return 2; }
if (!File.Exists(aabPath)) { Console.Error.WriteLine($"aab not found: {aabPath}"); return 2; }

var classes = new Dictionary<string, JClass>();
var platform = new Dictionary<string, JClass>();
var tmp = Path.Combine(Path.GetTempPath(), "check-r8-jni-" + Guid.NewGuid().ToString("N"));
List<(string Cls, char Kind, string Name, string Sig, string Asm)> members;
try {
  Directory.CreateDirectory(tmp);
  var dexCount = 0;
  using (var zip = ZipFile.OpenRead(aabPath))
    foreach (var e in zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^base/dex/classes\d*\.dex$"))) {
      var dst = Path.Combine(tmp, e.Name);
      e.ExtractToFile(dst);
      ParseDex(dst);
      dexCount++;
    }
  if (dexCount == 0) { Console.Error.WriteLine($"no base/dex/classes*.dex in {aabPath}"); return 2; }
  LoadPlatform(androidJar);
  members = Extract(asmDir).ToList();
} catch (Exception ex) {
  Console.Error.WriteLine($"error: {ex.Message}");
  return 2;
} finally {
  try { Directory.Delete(tmp, true); } catch (IOException) { }
}

// Binding-only members absent from the R8 input jars too, so R8 did not remove them. Null name or signature matches any.
var allowlist = new (string Cls, string? Name, string? Sig)[] {
  // Binding-only constructor; the Java class has no such constructor.
  ("com/google/android/material/shape/MaterialShapeDrawable$MaterialShapeDrawableState", "<init>", "(Landroid/graphics/drawable/Drawable$ConstantState;)V"),
  // Binding-only method; the Java class has no such method.
  ("androidx/appcompat/widget/ScrollingTabContainerView$VisibilityAnimListener", "withFinalVisibility", null),
  // Platform class hidden from the public android.jar; absent from the R8 input jars, and R8 never rewrites platform classes.
  ("android/text/SpannableStringInternal", null, null),
  // Platform method hidden from the public android.jar; the covariant ListAdapter override is not in the R8 input jars either.
  ("android/widget/AbsListView", "getAdapter", "()Landroid/widget/ListAdapter;"),
};

var seen = new HashSet<(string, char, string, string)>();
var missing = new List<string>();
foreach (var (cls, kind, name, sig, asm) in members) {
  if (!seen.Add((cls, kind, name, sig))) continue;
  if (cls.StartsWith("mono/")) continue;
  if (allowlist.Any(a => a.Cls == cls && (a.Name == null || a.Name == name) && (a.Sig == null || a.Sig == sig))) continue;
  var k = Find(cls);
  if (k == null) { missing.Add($"{cls} {name} {sig} {asm} (class not in DEX or android.jar)"); continue; }
  var ok = kind == 'F' ? Resolve(cls, c => c.Fields.Contains(name))
    : name == "<init>" ? k.Methods.Contains((name, sig))
    : Resolve(cls, c => c.Methods.Contains((name, sig)));
  if (!ok) missing.Add($"{cls} {name} {sig} {asm}");
}
foreach (var m in missing) Console.WriteLine(m);
Console.WriteLine($"dex_classes={classes.Count} platform_classes={platform.Count} members_checked={seen.Count} missing={missing.Count}");
return missing.Count == 0 ? 0 : 1;

static string? FindSdk() => Environment.GetEnvironmentVariable("ANDROID_HOME") ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

static string? FindDexdump() {
  var sdk = FindSdk();
  if (sdk == null || !Directory.Exists(Path.Combine(sdk, "build-tools"))) return null;
  var exe = OperatingSystem.IsWindows() ? "dexdump.exe" : "dexdump";
  return Directory.GetDirectories(Path.Combine(sdk, "build-tools"))
    .Select(d => (d, v: Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : new Version(0, 0)))
    .OrderByDescending(x => x.v).Select(x => Path.Combine(x.d, exe)).FirstOrDefault(File.Exists);
}

static string? FindAndroidJar() {
  var sdk = FindSdk();
  if (sdk == null || !Directory.Exists(Path.Combine(sdk, "platforms"))) return null;
  return Directory.GetDirectories(Path.Combine(sdk, "platforms"), "android-*")
    .Select(d => (d, v: decimal.TryParse(Path.GetFileName(d)["android-".Length..], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1m))
    .Where(x => x.v >= 0 && File.Exists(Path.Combine(x.d, "android.jar")))
    .OrderByDescending(x => x.v).Select(x => Path.Combine(x.d, "android.jar")).FirstOrDefault();
}

JClass? Find(string c) => classes.TryGetValue(c, out var k) ? k : platform.TryGetValue(c, out k) ? k : null;

// True when some class in the supertype chain, through DEX and android.jar classes alike, declares the member.
bool Resolve(string c, Func<JClass, bool> declares, HashSet<string>? visited = null) {
  visited ??= new();
  if (!visited.Add(c)) return false;
  var k = Find(c);
  if (k == null) return false;
  if (declares(k)) return true;
  foreach (var p in (k.Super == null ? Enumerable.Empty<string>() : new[] { k.Super }).Concat(k.Interfaces))
    if (Resolve(p, declares, visited)) return true;
  return false;
}

void ParseDex(string dex) {
  var psi = new ProcessStartInfo(dexdump!, $"\"{dex}\"") { RedirectStandardOutput = true, UseShellExecute = false };
  using var proc = Process.Start(psi)!;
  JClass? cur = null; string? sect = null, pend = null;
  string? line;
  while ((line = proc.StandardOutput.ReadLine()) != null) {
    var m = Rx.ClassDescriptor.Match(line);
    if (m.Success) {
      cur = classes.TryGetValue(m.Groups[1].Value, out var ex) ? ex : classes[m.Groups[1].Value] = new JClass();
      sect = null; continue;
    }
    if (cur == null) continue;
    var s = line.Trim();
    if (s.StartsWith("Superclass")) { var mm = Rx.TypeName.Match(s); cur.Super = mm.Success ? mm.Groups[1].Value : null; continue; }
    if (s.StartsWith("Interfaces")) { sect = "ifs"; continue; }
    if (s.StartsWith("Static fields") || s.StartsWith("Instance fields")) { sect = "f"; continue; }
    if (s.StartsWith("Direct methods") || s.StartsWith("Virtual methods")) { sect = "m"; continue; }
    if (s.StartsWith("source_file_idx")) { sect = null; continue; }
    if (sect == "ifs") { var mm = Rx.InterfaceRow.Match(s); if (mm.Success) cur.Interfaces.Add(mm.Groups[1].Value); continue; }
    if (sect is "f" or "m") {
      var n = Rx.NameRow.Match(s);
      if (n.Success) { pend = n.Groups[1].Value; continue; }
      var t = Rx.TypeRow.Match(s);
      if (t.Success && pend != null) {
        if (sect == "f") cur.Fields.Add(pend); else cur.Methods.Add((pend, t.Groups[1].Value));
        pend = null;
      }
    }
  }
  proc.WaitForExit();
  if (proc.ExitCode != 0) throw new InvalidOperationException($"dexdump failed on {dex}");
}

void LoadPlatform(string jar) {
  using var zip = ZipFile.OpenRead(jar);
  foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".class", StringComparison.Ordinal))) {
    using var s = e.Open();
    using var ms = new MemoryStream();
    s.CopyTo(ms);
    var k = ParseClassFile(ms.ToArray(), out var name);
    platform[name] = k;
  }
}

// Minimal JVM class-file reader (JVMS 4.1): constant pool, this/super/interfaces, field names, method name+descriptor.
static JClass ParseClassFile(byte[] b, out string thisName) {
  var p = 8; // magic, minor, major
  ushort U2() { var v = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p)); p += 2; return v; }
  var n = U2();
  var utf = new string?[n];
  var cls = new ushort[n];
  for (var i = 1; i < n; i++) {
    var tag = b[p++];
    switch (tag) {
      case 1: { int len = U2(); utf[i] = Encoding.UTF8.GetString(b, p, len); p += len; break; } // modified UTF-8; ASCII-safe for names and descriptors
      case 7: cls[i] = U2(); break;
      case 8: case 16: case 19: case 20: p += 2; break;
      case 15: p += 3; break;
      case 3: case 4: case 9: case 10: case 11: case 12: case 17: case 18: p += 4; break;
      case 5: case 6: p += 8; i++; break;
      default: throw new InvalidDataException($"unknown constant pool tag {tag}");
    }
  }
  string ClassName(int idx) => utf[cls[idx]]!;
  U2(); // access
  thisName = ClassName(U2());
  var k = new JClass();
  var sup = U2();
  k.Super = sup == 0 ? null : ClassName(sup);
  var ic = U2();
  for (var i = 0; i < ic; i++) k.Interfaces.Add(ClassName(U2()));
  for (var pass = 0; pass < 2; pass++) {
    var count = U2();
    for (var i = 0; i < count; i++) {
      U2(); var nm = utf[U2()]!; var desc = utf[U2()]!;
      var ac = U2();
      for (var a = 0; a < ac; a++) { U2(); p += 4 + (int)(BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p))); }
      if (pass == 0) k.Fields.Add(nm); else k.Methods.Add((nm, desc));
    }
  }
  return k;
}

static IEnumerable<(string Cls, char Kind, string Name, string Sig, string Asm)> Extract(string dir) {
  foreach (var path in Directory.GetFiles(dir, "*.dll")) {
    using var fs = File.OpenRead(path);
    PEReader pe; MetadataReader md;
    try { pe = new PEReader(fs); if (!pe.HasMetadata) continue; md = pe.GetMetadataReader(); } catch (BadImageFormatException) { continue; }
    var asm = Path.GetFileName(path);
    foreach (var th in md.TypeDefinitions) {
      var t = md.GetTypeDefinition(th);
      var ctx = $"{md.GetString(t.Namespace)}.{md.GetString(t.Name)} in {asm}";
      var reg = Reg(md, t.GetCustomAttributes(), ctx);
      if (reg == null || !reg.Value.doNotAcw) continue;
      var cls = reg.Value.name;
      foreach (var mh in t.GetMethods()) {
        var r = Reg(md, md.GetMethodDefinition(mh).GetCustomAttributes(), ctx);
        if (r?.sig != null) yield return (cls, 'M', Norm(r.Value.name), r.Value.sig, asm);
      }
      foreach (var ph in t.GetProperties()) {
        var p = md.GetPropertyDefinition(ph);
        var r = Reg(md, p.GetCustomAttributes(), ctx);
        if (r != null) yield return r.Value.sig != null ? (cls, 'M', Norm(r.Value.name), r.Value.sig, asm) : (cls, 'F', r.Value.name, "", asm);
        var acc = p.GetAccessors();
        foreach (var a in new[] { acc.Getter, acc.Setter }) {
          if (a.IsNil) continue;
          var ra = Reg(md, md.GetMethodDefinition(a).GetCustomAttributes(), ctx);
          if (ra?.sig != null) yield return (cls, 'M', Norm(ra.Value.name), ra.Value.sig, asm);
        }
      }
      foreach (var fh in t.GetFields()) {
        var f = md.GetFieldDefinition(fh);
        if ((f.Attributes & FieldAttributes.Literal) != 0) continue; // C# const: no JNI read
        var r = Reg(md, f.GetCustomAttributes(), ctx);
        if (r != null) yield return (cls, 'F', r.Value.name, "", asm);
      }
    }
  }
}

static string Norm(string n) => n == ".ctor" ? "<init>" : n;

static (string name, string? sig, bool doNotAcw)? Reg(MetadataReader md, CustomAttributeHandleCollection cas, string ctx) {
  foreach (var ch in cas) {
    var ca = md.GetCustomAttribute(ch);
    string tn = "";
    if (ca.Constructor.Kind == HandleKind.MemberReference) {
      var mr = md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
      if (mr.Parent.Kind == HandleKind.TypeReference) { var tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent); tn = md.GetString(tr.Namespace) + "." + md.GetString(tr.Name); }
    } else if (ca.Constructor.Kind == HandleKind.MethodDefinition) {
      var mdef = md.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor); var td = md.GetTypeDefinition(mdef.GetDeclaringType()); tn = md.GetString(td.Namespace) + "." + md.GetString(td.Name);
    }
    if (tn != "Android.Runtime.RegisterAttribute") continue;
    try {
      var br = md.GetBlobReader(ca.Value);
      if (br.ReadUInt16() != 1) throw new InvalidDataException("bad prolog");
      var name = br.ReadSerializedString()!;
      string? sig = null;
      var n = ParamCount(md, ca.Constructor); // 1 = name; 2 = name+sig; 3 = name+sig+connector
      if (n >= 2) { sig = br.ReadSerializedString(); if (n >= 3) br.ReadSerializedString(); }
      var dna = false;
      int named = br.ReadUInt16();
      for (var i = 0; i < named; i++) {
        br.ReadByte(); // 0x53 field / 0x54 property
        var et = br.ReadByte();
        if (et == 0x55) br.ReadSerializedString(); // enum type name
        var nm = br.ReadSerializedString();
        switch (et) {
          case 0x02: { var v = br.ReadBoolean(); if (nm == "DoNotGenerateAcw") dna = v; break; }
          case 0x08: case 0x55: br.ReadInt32(); break;
          case 0x0e: br.ReadSerializedString(); break;
          default: throw new InvalidDataException($"unsupported named-argument type 0x{et:x2} for '{nm}'");
        }
      }
      return (name, sig, dna);
    } catch (Exception ex) when (ex is InvalidDataException or BadImageFormatException or ArgumentOutOfRangeException) {
      throw new InvalidDataException($"unparseable [Register] on {ctx}: {ex.Message}", ex);
    }
  }
  return null;
}

static int ParamCount(MetadataReader md, EntityHandle ctor) {
  var sh = ctor.Kind == HandleKind.MemberReference ? md.GetMemberReference((MemberReferenceHandle)ctor).Signature : md.GetMethodDefinition((MethodDefinitionHandle)ctor).Signature;
  var r = md.GetBlobReader(sh); r.ReadSignatureHeader(); return r.ReadCompressedInteger();
}

static class Rx {
  public static readonly Regex ClassDescriptor = new(@"^\s*Class descriptor\s*: 'L(.+);'", RegexOptions.Compiled);
  public static readonly Regex TypeName = new("'L(.+);'", RegexOptions.Compiled);
  public static readonly Regex InterfaceRow = new(@"^#\d+\s*: 'L(.+);'", RegexOptions.Compiled);
  public static readonly Regex NameRow = new(@"^name\s*: '(.+)'", RegexOptions.Compiled);
  public static readonly Regex TypeRow = new(@"^type\s*: '(.+)'", RegexOptions.Compiled);
}

class JClass {
  public string? Super;
  public List<string> Interfaces = new();
  public HashSet<string> Fields = new();
  public HashSet<(string, string)> Methods = new();
}
