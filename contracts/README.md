# Contracts

Wire-format fixtures shared by the .NET and Swift suites.

## Device backup tests

Two local-only tests read every row of the seven tables from a real device backup: `DeviceBackupFixtureTests` in `PrayerApp.Tests` and in `ios/PrayerKit`.

- `PP_DEVICE_BACKUP_DB` is the absolute path to a copy of the device `Library/prayer_app.db`.
- When the variable is unset or the file is missing, both tests skip.
- The backup is never committed or copied into the repo. The tests open it read-only.
- Failures name the table and the error type only, never a column value.

```
PP_DEVICE_BACKUP_DB=/path/to/prayer_app.db dotnet test PrayerApp.Tests/PrayerApp.Tests.csproj --filter FullyQualifiedName~DeviceBackupFixtureTests
PP_DEVICE_BACKUP_DB=/path/to/prayer_app.db swift test --package-path ios/PrayerKit --filter DeviceBackupFixtureTests
```
