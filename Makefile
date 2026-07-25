SOLUTION := Cynara.Api.sln
CONFIGURATION := Debug
SONAR_COMPOSE := docker/sonarqube/docker-compose.yml
SEED_ARGS ?=

.PHONY: restore fmt fmt\:check lint lint\:fix test check fix seed \
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
		--logger "console;verbosity=normal" --filter "Category!=E2E"

check: restore fmt\:check lint test

fix: restore fmt
	dotnet format $(SOLUTION) style --severity info
	dotnet format $(SOLUTION) analyzers --severity info

seed:
	dotnet run --project tools/Cynara.Seed -c $(CONFIGURATION) -- $(SEED_ARGS)

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
