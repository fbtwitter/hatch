# Generates PNG assets from ../assets/logo.svg (repo root — shared with mobile/ and
# README.md) via the AssetGen helper project.
# Invoked from CI (ci.yml, release.yml); re-run manually any time the Assets folder
# is missing or logo.svg changes.
dotnet run --project "$PSScriptRoot\AssetGen\AssetGen.csproj" -- "$PSScriptRoot\..\..\assets\logo.svg" "$PSScriptRoot\..\Assets"
