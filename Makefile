# Setup and Release builds on macOS and Linux (README.md, Build). On Windows use the commands of README.md.
#   make setup    install the build dependencies, build the MonoGame native runtime, restore the solution
#   make server   build the server, the world importer and the account tool
#   make client   build the client and the effect importer
#   make run-server   start the server in Docker (docker-compose.yml), importing the world data first when data/ has none
#   make run-client   build and start the client, which connects to localhost; RESOLUTION=2560x1440 sets the window
#                     size for that start

DOTNET_BUILD = dotnet build -c Release -m:1 -warnaserror
VULKAN_SDK_URL = https://sdk.lunarg.com/sdk/download/latest/mac/vulkan_sdk.zip
VULKAN_SDK_WORK = monogame/runtime/work/vulkan-sdk
FIND_VULKAN_SDK = sdk="$(VULKAN_SDK)"; [ -n "$$sdk" ] || sdk=$$(ls -d "$$HOME"/VulkanSDK/*/macOS 2> /dev/null | tail -1)
WORLD_IMPORT = tools/PristonTale.WorldImport/PristonTale.WorldImport.csproj
DATABASE = data/pristontale.db
OS := $(shell uname -s)

.PHONY: help setup server client run-server run-client deps vulkan-sdk runtime restore

help:
	@echo "make setup    install the build dependencies, build the MonoGame native runtime, restore the solution"
	@echo "make server   build the server, the world importer and the account tool"
	@echo "make client   build the client and the effect importer (needs make setup first)"
	@echo "make run-server   start the server in Docker, importing the world data first when data/ has none"
	@echo "make run-client   build and start the client, which connects to localhost (RESOLUTION=WIDTHxHEIGHT for the window size)"

setup: deps vulkan-sdk runtime restore

ifeq ($(OS),Darwin)
deps:
	@xcode-select -p > /dev/null 2>&1 || { xcode-select --install; echo "Finish the Xcode command line tools install, then run make setup again." >&2; exit 1; }
	@command -v brew > /dev/null || { echo "Install Homebrew first (https://brew.sh), then run make setup again." >&2; exit 1; }
	@dotnet --list-sdks 2> /dev/null | grep -q '^10\.' || brew install --cask dotnet-sdk
	@for formula in cmake premake pkg-config; do brew list --formula "$$formula" > /dev/null 2>&1 || brew install "$$formula"; done

# The runtime links MoltenVK from the LunarG Vulkan SDK (lib/MoltenVK.xcframework). Its installer opens so its licence
# is accepted by the user; the default location ~/VulkanSDK/<version> is found by the runtime step.
vulkan-sdk:
	@$(FIND_VULKAN_SDK); \
	if [ -n "$$sdk" ] && [ -d "$$sdk/lib/MoltenVK.xcframework" ]; then echo "Vulkan SDK: $$sdk"; exit 0; fi; \
	rm -rf "$(VULKAN_SDK_WORK)"; mkdir -p "$(VULKAN_SDK_WORK)"; \
	echo "Downloading the Vulkan SDK for macOS (about 400 MB)"; \
	curl -fL --progress-bar -o "$(VULKAN_SDK_WORK)/vulkan_sdk.zip" "$(VULKAN_SDK_URL)"; \
	unzip -q "$(VULKAN_SDK_WORK)/vulkan_sdk.zip" -d "$(VULKAN_SDK_WORK)"; \
	installer=$$(find "$(VULKAN_SDK_WORK)" -maxdepth 2 -name '*.app' | head -1); \
	[ -n "$$installer" ] || { echo "The Vulkan SDK download holds no installer." >&2; exit 1; }; \
	echo "Install the SDK in the window that opens (default location ~/VulkanSDK), then close the installer."; \
	open -W "$$installer"; \
	rm -rf "$(VULKAN_SDK_WORK)"; \
	ls -d "$$HOME"/VulkanSDK/*/macOS/lib/MoltenVK.xcframework > /dev/null 2>&1 || { echo "The Vulkan SDK is not in ~/VulkanSDK; pass VULKAN_SDK=<sdk>/macOS to make." >&2; exit 1; }

runtime:
	@$(FIND_VULKAN_SDK); \
	VULKAN_SDK="$$sdk" bash monogame/runtime/build.sh
else ifeq ($(OS),Linux)
deps:
	@if command -v apt-get > /dev/null; then \
	  sudo apt-get install -y cmake make g++ pkg-config git libasound2-dev libpulse-dev libpipewire-0.3-dev; \
	elif command -v dnf > /dev/null; then \
	  sudo dnf install -y cmake make gcc-c++ pkg-config git alsa-lib-devel pulseaudio-libs-devel pipewire-devel; \
	else \
	  echo "Install cmake, make, a C++17 compiler, pkg-config, git and the ALSA, PulseAudio and PipeWire headers." >&2; exit 1; \
	fi
	@command -v premake5 > /dev/null || { echo "Install premake5 (https://premake.github.io), then run make setup again." >&2; exit 1; }
	@dotnet --list-sdks 2> /dev/null | grep -q '^10\.' || { echo "Install the .NET 10 SDK (https://dot.net), then run make setup again." >&2; exit 1; }

# build.sh uses the vendored Vulkan headers on Linux; the SDK is not needed.
vulkan-sdk:

runtime:
	bash monogame/runtime/build.sh
else
deps vulkan-sdk runtime:
	@echo "Unsupported system $(OS): on Windows use the commands of README.md." >&2; exit 1
endif

restore:
	dotnet tool restore
	dotnet restore PristonTale.sln -warnaserror

server:
	$(DOTNET_BUILD) Server/PristonTale.Server/PristonTale.Server.csproj
	$(DOTNET_BUILD) $(WORLD_IMPORT)
	$(DOTNET_BUILD) tools/PristonTale.DevAccounts/PristonTale.DevAccounts.csproj

client:
	$(DOTNET_BUILD) Client/PristonTale/PristonTale.csproj
	$(DOTNET_BUILD) tools/PristonTale.VfxImport/PristonTale.VfxImport.csproj

# The server applies migrations but does not import, so a missing database is imported on the host first (a failed
# import leaves none behind). Run the import again by hand after new migrations or data (README.md, Run); it keeps
# accounts and characters.
.DELETE_ON_ERROR:
$(DATABASE):
	$(DOTNET_BUILD) $(WORLD_IMPORT)
	dotnet run --project $(WORLD_IMPORT) -c Release --no-build -- import --sqlite $(DATABASE) --source assets/GameServer --assets assets
	dotnet run --project $(WORLD_IMPORT) -c Release --no-build -- verify --sqlite $(DATABASE)

# In the foreground: Ctrl-C stops the container.
run-server: $(DATABASE)
	@docker info > /dev/null 2>&1 || { echo "Docker is not running: start Docker Desktop (or the Docker daemon) first." >&2; exit 1; }
	HOST_UID=$$(id -u) HOST_GID=$$(id -g) docker compose --profile server up --build server

run-client: client
	dotnet run --project Client/PristonTale -c Release --no-build $(if $(RESOLUTION),-- --resolution $(RESOLUTION))
