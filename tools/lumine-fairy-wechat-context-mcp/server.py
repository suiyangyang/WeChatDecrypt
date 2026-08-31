#!/usr/bin/env python3
"""Read-only stdio MCP bridge for the Lumine.Fairy WeChat context API.

The bridge deliberately contains no database access. It forwards only the
conversation identity fields required by the context resolver.
"""

from __future__ import annotations

import json
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any


TOOL_NAME = "resolve_wechat_context"
PROTOCOL_VERSION = "2024-11-05"
CONFIG_PATH = Path(__file__).with_name("wechat-context.local.json")


def fail(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


def get_config() -> tuple[str, str, float]:
    try:
        config = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise RuntimeError(f"local MCP config is missing: {CONFIG_PATH.name}") from exc
    except (OSError, json.JSONDecodeError) as exc:
        raise RuntimeError("local MCP config cannot be read") from exc

    if not isinstance(config, dict):
        raise RuntimeError("local MCP config must be a JSON object")

    host = str(config.get("host", "")).strip()
    token = str(config.get("token", "")).strip()
    timeout_text = str(config.get("timeoutSeconds", "10")).strip()
    try:
        timeout = max(1.0, min(float(timeout_text), 60.0))
    except ValueError:
        timeout = 10.0

    try:
        port = int(config.get("port", 18766))
    except (TypeError, ValueError) as exc:
        raise RuntimeError("local MCP config port must be an integer") from exc

    if not host:
        raise RuntimeError("local MCP config host is not configured")
    if any(character in host for character in "/\\: "):
        raise RuntimeError("local MCP config host must be a host name or IP address")
    if not 1 <= port <= 65535:
        raise RuntimeError("local MCP config port must be between 1 and 65535")
    if not token:
        raise RuntimeError("local MCP config token is not configured")
    return f"http://{host}:{port}", token, timeout


def send(message: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(message, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def error_response(request_id: Any, message: str, code: int = -32602) -> None:
    send({"jsonrpc": "2.0", "id": request_id, "error": {"code": code, "message": message}})


def handle_tools_call(request_id: Any, params: dict[str, Any]) -> None:
    name = params.get("name")
    if name != TOOL_NAME:
        error_response(request_id, f"unknown tool: {name}")
        return

    arguments = params.get("arguments") or {}
    if not isinstance(arguments, dict):
        error_response(request_id, "arguments must be an object")
        return
    allowed = {"account_id", "wechat_username", "display_name", "is_group_chat"}
    if set(arguments) - allowed:
        error_response(request_id, "unsupported tool arguments")
        return
    required = ("account_id", "wechat_username", "display_name")
    if any(not isinstance(arguments.get(key), str) or not arguments[key].strip() for key in required):
        error_response(request_id, "account_id, wechat_username and display_name are required")
        return
    if "is_group_chat" in arguments and not isinstance(arguments["is_group_chat"], bool):
        error_response(request_id, "is_group_chat must be a boolean")
        return

    try:
        base_url, token, timeout = get_config()
        payload = {
            "accountId": arguments["account_id"].strip(),
            "weChatUsername": arguments["wechat_username"].strip(),
            "displayName": arguments["display_name"].strip(),
            "isGroupChat": bool(arguments.get("is_group_chat", False)),
        }
        request = urllib.request.Request(
            base_url + "/api/v1/wechat-context/resolve",
            data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
            headers={
                "Authorization": "Bearer " + token,
                "Content-Type": "application/json",
            },
            method="POST",
        )
        with urllib.request.urlopen(request, timeout=timeout) as response:
            result = json.loads(response.read().decode("utf-8"))
        text = json.dumps(result, ensure_ascii=False, indent=2)
        send({"jsonrpc": "2.0", "id": request_id, "result": {"content": [{"type": "text", "text": text}]}})
    except urllib.error.HTTPError as exc:
        error_response(request_id, f"context API returned HTTP {exc.code}", -32001)
    except (urllib.error.URLError, TimeoutError) as exc:
        error_response(request_id, f"context API is unavailable: {exc.reason if isinstance(exc, urllib.error.URLError) else exc}", -32002)
    except (RuntimeError, ValueError, json.JSONDecodeError) as exc:
        error_response(request_id, str(exc), -32003)


def handle(request: dict[str, Any]) -> None:
    request_id = request.get("id")
    method = request.get("method")
    params = request.get("params") or {}

    if method == "initialize":
        send({
            "jsonrpc": "2.0",
            "id": request_id,
            "result": {
                "protocolVersion": PROTOCOL_VERSION,
                "capabilities": {"tools": {}},
                "serverInfo": {"name": "lumine-fairy-wechat-context", "version": "1.0.0"},
            },
        })
    elif method == "notifications/initialized":
        return
    elif method == "tools/list":
        send({
            "jsonrpc": "2.0",
            "id": request_id,
            "result": {
                "tools": [{
                    "name": TOOL_NAME,
                    "description": "Resolve local read-only WeChat business context. Never returns chat content or private contact fields.",
                    "inputSchema": {
                        "type": "object",
                        "properties": {
                            "account_id": {"type": "string", "description": "WeChat account ID"},
                            "wechat_username": {"type": "string", "description": "Stable WeChat conversation username"},
                            "display_name": {"type": "string", "description": "Current conversation display name"},
                            "is_group_chat": {"type": "boolean", "default": False},
                        },
                        "required": ["account_id", "wechat_username", "display_name"],
                        "additionalProperties": False,
                    },
                }]
            },
        })
    elif method == "tools/call":
        handle_tools_call(request_id, params)
    elif method == "ping":
        send({"jsonrpc": "2.0", "id": request_id, "result": {}})
    elif request_id is not None:
        error_response(request_id, f"method not found: {method}", -32601)


def main() -> int:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            request = json.loads(line)
            if not isinstance(request, dict):
                error_response(None, "request must be an object")
                continue
            handle(request)
        except json.JSONDecodeError:
            error_response(None, "invalid JSON", -32700)
        except Exception as exc:  # Do not leak environment values or request bodies.
            fail(f"MCP bridge error: {type(exc).__name__}")
            error_response(request.get("id") if isinstance(request, dict) else None, "internal MCP bridge error", -32603)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
