using AppGeek.Models;
using AppGeek.Services;
using AppGeek.Tests;

// AppGeek's test harness. Run it with:
//     dotnet run --project tests/AppGeek.Tests -c Release
// Exit code 0 means everything passed; 1 means something did not, and CI fails the build.

ScopeTests.Run();
MatcherTests.Run();
ParserTests.Run();
VersionTests.Run();
ProgressTests.Run();
SecurityTests.Run();

return Check.Report();
