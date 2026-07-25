SOLUTION := Cynara.Api.sln
CONFIGURATION := Debug
SONAR_COMPOSE := docker/sonarqube/docker-compose.yml
MSSQL_COMPOSE := docker/mssql/docker-compose.yml
SEED_ARGS ?=

.PHONY: restore fmt fmt\:check lint lint\:fix test check fix seed \
	mssql-up mssql-down mssql-logs \
	sonar-up sonar-down sonar-bootstrap sonar-scan sonar

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
	dotnet test $(SOLUTION) --no-restore -c $(CONFIGURATION) --verbosity normal \
		--logger "console;verbosity=normal"

check: restore fmt\:check lint test

fix: restore fmt
	dotnet format $(SOLUTION) style --severity info
	dotnet format $(SOLUTION) analyzers --severity info

seed: mssql-up
	dotnet run --project tools/Cynara.Seed -c $(CONFIGURATION) -- $(SEED_ARGS)

mssql-up:
	docker compose -f $(MSSQL_COMPOSE) up -d

mssql-down:
	docker compose -f $(MSSQL_COMPOSE) down

mssql-logs:
	docker compose -f $(MSSQL_COMPOSE) logs -f mssql

sonar-up:
	docker compose -f $(SONAR_COMPOSE) up -d

sonar-down:
	docker compose -f $(SONAR_COMPOSE) down

sonar-bootstrap: sonar-up
	chmod +x scripts/sonar-bootstrap.sh
	./scripts/sonar-bootstrap.sh

sonar-scan:
	chmod +x scripts/sonar-scan.sh
	./scripts/sonar-scan.sh

sonar: sonar-bootstrap sonar-scan
