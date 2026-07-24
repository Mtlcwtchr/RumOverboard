# SETUP_GRAPHIFY

Интеграция [Graphify](https://github.com/Graphify-Labs/graphify) в RumOverboard.

## Что уже сделано в репозитории

- Добавлены project-scoped skill-файлы для Claude Code: `.claude/`.
- Добавлены cross-framework skill-файлы: `.agents/skills/graphify/`.
- В `CLAUDE.md` добавлены правила использования Graphify.
- Добавлен `.claudeignore` для исключения `graphify-out/` из prompt cache.
- В `.gitignore` добавлены артефакты Graphify (`graphify-out/`, `graph.json`).

## Локальная установка CLI

> Официальный PyPI-пакет называется `graphifyy` (двойной `y`), команда CLI — `graphify`.

```bash
uv tool install "graphifyy[mcp]"
```

Проверка:

```bash
graphify --help
```

## Project setup (если нужно повторить)

```bash
cd /Users/antonslauta/RumOverboard
graphify install --project
graphify install --project --platform agents
```

## Первый прогон графа (без API-ключей)

`--code-only` использует AST и не требует LLM API ключей.

```bash
cd /Users/antonslauta/RumOverboard
graphify extract . --code-only
```

Быстрый запрос по графу:

```bash
graphify query "ship buoyancy physics"
```

Инкрементальное обновление после изменений:

```bash
graphify update .
```

## MCP сервер (опционально)

`graphify-mcp` доступен после установки `graphifyy[mcp]`.

```bash
# stdio (локальный агент)
graphify-mcp --graph graphify-out/graph.json --transport stdio

# HTTP (shared endpoint)
graphify-mcp --graph graphify-out/graph.json --transport http --host 127.0.0.1 --port 8080 --path /mcp
```

## Примечания

- Выходные данные пишутся в `graphify-out/` рядом с рабочей директорией запуска команды.
- Для этого проекта рекомендовано запускать команды из корня репозитория.
- `graphify-out/` не коммитим в git.

