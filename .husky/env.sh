export NuGetAudit=false

if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "$HOME/.dotnet/dotnet" ]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$DOTNET_ROOT:$PATH"
  elif [ -x "/usr/share/dotnet/dotnet" ]; then
    export DOTNET_ROOT="/usr/share/dotnet"
    export PATH="$DOTNET_ROOT:$PATH"
  fi
fi
