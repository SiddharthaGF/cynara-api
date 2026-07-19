SOLUTION := Cynara.Api.sln
CONFIGURATION := Debug

.PHONY: restore format format-check lint test check fix

restore:
	dotnet restore $(SOLUTION)

format:
	dotnet format $(SOLUTION)

format-check:
	dotnet format $(SOLUTION) --verify-no-changes

lint:
	dotnet build $(SOLUTION) --no-restore -warnaserror -c $(CONFIGURATION)

test:
	dotnet test $(SOLUTION) --no-restore -c $(CONFIGURATION) --verbosity minimal

check: restore format-check lint test

fix: restore format
	dotnet format $(SOLUTION) style --severity info
	dotnet format $(SOLUTION) analyzers --severity info
