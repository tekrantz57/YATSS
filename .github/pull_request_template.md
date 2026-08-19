## Summary

-

## Validation

- [ ] `dotnet build YATSSWin\YATSS.sln -c Release`
- [ ] `dotnet run --project YATSSWin\YATSS.Tests\YATSS.Tests.csproj -c Release`
- [ ] Relevant `arduino-cli compile` command, if controller firmware changed
- [ ] Manual hardware or UI validation, if behavior changed

## Safety And Data

- [ ] Track-power, relay, watchdog, or firmware-update behavior was considered
- [ ] Documentation or smoke-test notes were updated, if needed
- [ ] No race databases, reports, serial logs with private data, credentials, or local machine configuration are included
