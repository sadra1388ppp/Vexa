#!/usr/bin/env bash
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"
: "${ConnectionStrings__DefaultConnection:?Set ConnectionStrings__DefaultConnection to the MariaDB database to scaffold.}"
project="$root/tools/Vexa.Scaffolding/Vexa.Scaffolding.csproj"
stage="$root/tools/Vexa.Scaffolding/.generated"
trap 'rm -rf -- "$stage"' EXIT
rm -rf -- "$stage"

dotnet tool restore
dotnet ef dbcontext scaffold 'Name=ConnectionStrings:DefaultConnection' Pomelo.EntityFrameworkCore.MySql \
  --project "$project" --startup-project "$project" \
  --context AppDbContext --context-dir "$stage/Data" --output-dir "$stage/Entities" \
  --namespace Vexa.Server.Entities --context-namespace Vexa.Server.Data \
  --table Users --table Chats --table ChatMembers --table Messages --table Contacts \
  --no-onconfiguring --force

# Do not overwrite the current model when a required table is missing.
for entity in User Chat ChatMember Message Contact; do
  test -s "$stage/Entities/$entity.cs" || { echo "Missing generated entity: $entity" >&2; exit 1; }
done
test -s "$stage/Data/AppDbContext.cs"
mkdir -p "$root/Vexa.Server/Entities/Generated" "$root/Vexa.Server/Data/Generated"
cp "$stage/Entities/"*.cs "$root/Vexa.Server/Entities/Generated/"
cp "$stage/Data/AppDbContext.cs" "$root/Vexa.Server/Data/Generated/AppDbContext.cs"
echo 'Entities and DbContext updated. Review the generated diff, then build the solution.'
