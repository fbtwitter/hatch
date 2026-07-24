# Generates PNG assets from logo.svg via the AssetGen helper project.
# Run automatically by the EnsureAssets MSBuild target on first build.
# Re-run manually any time the Assets folder is missing or logo.svg changes.
dotnet run --project "$PSScriptRoot\AssetGen\AssetGen.csproj" -- logo.svg Assets
