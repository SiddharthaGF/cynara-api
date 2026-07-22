SOLUTION := Cynara.Api.sln
CONFIGURATION := Debug

.PHONY: restore fmt fmt\:check lint lint\:fix test check fix

restore:
	dotnet restore $(SOLUTION)

fmt:
	dotnet format $(SOLUTION)

fmt\:check:
	dotnet format $(SOLUTION) --verify-no-changes

lint:
	dotnet build $(SOLUTION) --no-restore -warnaserror -c $(CONFIGURATION) --verbosity normal

lint\:fix: restore
	dotnet format $(SOLUTION) analyzers --severity info --verbosity diagnostic
	$(MAKE) lint

test: restore
	dotnet test $(SOLUTION) --no-restore -c $(CONFIGURATION) --verbosity normal --logger "console;verbosity=normal"

check: restore fmt\:check lint test

fix: restore fmt
	dotnet format $(SOLUTION) style --severity info
	dotnet format $(SOLUTION) analyzers --severity info
