# Raindrop NuGet Package Makefile
# 用法:
#   make build          打包所有 src 项目到本地 NuGet 仓库
#   make push-server    将本地仓库中的包推送到远程 NuGet 服务器
#   make publish        build + push-server 一键执行
#   make clean          清理本地仓库
#   make list-local     列出本地仓库中的包
#
# 环境变量 (推送服务器时需要):
#   NUGET_SERVER_URL    你的 NuGet 服务器地址
#   NUGET_API_KEY       你的 NuGet API Key
#
# 示例:
#   export NUGET_SERVER_URL="http://your-server:8080/v3/index.json"
#   export NUGET_API_KEY="your-api-key"
#   make push-server

# ── 配置 ──────────────────────────────────────────────
VERSION        := 0.0.1
LOCAL_REPO     := $(CURDIR)/nuget-local
NUGET_SOURCE   := raindrop-local
REMOTE_SOURCE  := $(or $(NUGET_SERVER_URL),<UNCONFIGURED>)
REMOTE_API_KEY := $(or $(NUGET_API_KEY),<UNCONFIGURED>)

# ── 内部变量 ──────────────────────────────────────────
SOLUTION       := $(CURDIR)/Raindrop.slnx
PACK_ARGS      := -p:Version=$(VERSION) -p:GeneratePackageOnBuild=false

# ── 辅助：检测是否配置了远程服务器 ────────────────────
ifeq ($(REMOTE_SOURCE),<UNCONFIGURED>)
  REMOTE_CONFIGURED := 0
else
  REMOTE_CONFIGURED := 1
endif

.PHONY: build push-server publish clean list-local

# ── build: 编译并以 Release 配置打包到本地 NuGet 仓库 ──
build:
	@echo "=== Build & pack v$(VERSION) -> $(LOCAL_REPO) ==="
	@mkdir -p "$(LOCAL_REPO)"
	dotnet pack "$(SOLUTION)" --nologo -v q -c Release $(PACK_ARGS) -o "$(LOCAL_REPO)"
	@echo ""
	@echo "=== Generated packages ==="
	@ls -1 "$(LOCAL_REPO)"/*.nupkg 2>/dev/null || echo "  (no packages)"
	@echo ""
	@echo "=== Register local NuGet source (if needed) ==="
	@dotnet nuget list source | grep -q "$(NUGET_SOURCE)" 2>/dev/null || \
		dotnet nuget add source "$(LOCAL_REPO)" --name "$(NUGET_SOURCE)"
	@echo ""
	@echo "Done. Local repo: $(LOCAL_REPO)"
	@echo "Other projects can use NuGet source '$(NUGET_SOURCE)' to consume packages."

# ── push-server: 推送本地包到远程 NuGet 服务器 ────────
push-server:
	@if [ "$(REMOTE_CONFIGURED)" = "0" ]; then \
		echo "=== ERROR: Remote NuGet server not configured ==="; \
		echo ""; \
		echo "  Please set environment variables first:"; \
		echo '    export NUGET_SERVER_URL="http://your-server:8080/v3/index.json"'; \
		echo '    export NUGET_API_KEY="your-api-key"'; \
		echo ""; \
		exit 1; \
	fi
	@echo "=== Push to remote server ==="
	@echo "  Local:  $(LOCAL_REPO)"
	@echo "  Remote: $(REMOTE_SOURCE)"
	@for f in "$(LOCAL_REPO)"/*.nupkg; do \
		if [ -f "$$f" ]; then \
			echo "  Pushing: $$(basename "$$f")"; \
			dotnet nuget push "$$f" \
				--source "$(REMOTE_SOURCE)" \
				--api-key "$(REMOTE_API_KEY)" \
				--skip-duplicate || exit 1; \
		fi; \
	done
	@echo ""
	@echo "Done push-server."

# ── publish: build + push-server 一键执行 ─────────────
publish: build push-server
	@echo "Done publish."

# ── clean: 清理本地仓库 ───────────────────────────────
clean:
	@echo "=== Clean local NuGet repo ==="
	@rm -rf "$(LOCAL_REPO)"
	@echo "Deleted $(LOCAL_REPO)"

# ── list-local: 列出本地仓库中的包 ──────────────────────
list-local:
	@echo "=== Local repo: $(LOCAL_REPO) ==="
	@ls -1 "$(LOCAL_REPO)"/*.nupkg 2>/dev/null || echo "  (no packages)"
