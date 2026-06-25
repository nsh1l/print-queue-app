using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PrintQueueApp.WinUI
{
    /// <summary>
    /// MCP JSON-RPC server over stdio.
    /// Reads JSON-RPC requests from stdin, writes responses to stdout.
    /// Implements the WinUI tools by delegating to the MainWindow.
    /// </summary>
    public class MCPServerStdio
    {
        private readonly MCPServer _server;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public MCPServerStdio(MCPServer server)
        {
            _server = server;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var reader = new StreamReader(Console.OpenStandardInput);

            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var response = await HandleAsync(line, ct);
                    if (response != null)
                    {
                        await _writeLock.WaitAsync(ct);
                        try
                        {
                            Console.WriteLine(response);
                        }
                        finally
                        {
                            _writeLock.Release();
                        }
                    }
                }
                catch (Exception ex)
                {
                    var error = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = JsonValue.Create(1),
                        ["error"] = new JsonObject
                        {
                            ["code"] = -32603,
                            ["message"] = ex.Message
                        }
                    };
                    await _writeLock.WaitAsync(ct);
                    try
                    {
                        Console.WriteLine(error.ToJsonString());
                    }
                    finally
                    {
                        _writeLock.Release();
                    }
                }
            }
        }

        private async Task<string?> HandleAsync(string line, CancellationToken ct)
        {
            var node = JsonNode.Parse(line);
            if (node == null) return null;

            var json = node as JsonObject;
            if (json == null) return null;

            long id = 1;
            if (json.TryGetPropertyValue("id", out var jid) && jid != null)
                id = jid.GetValue<long>();

            string method = "";
            if (json.TryGetPropertyValue("method", out var jmethod) && jmethod != null)
                method = jmethod.GetValue<string>();

            JsonObject? parameters = null;
            if (json.TryGetPropertyValue("params", out var jparams) && jparams != null)
                parameters = jparams as JsonObject;

            object? result = null;

            switch (method)
            {
                case "initialize":
                    result = new JsonObject
                    {
                        ["protocolVersion"] = "2024-11-05",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject
                        {
                            ["name"] = "winui-mcp",
                            ["version"] = "1.0.0"
                        }
                    };
                    break;

                case "tools/list":
                    result = new JsonObject { ["tools"] = new JsonArray(GetToolDefs()) };
                    break;

                case "tools/call":
                    if (parameters != null)
                    {
                        var toolName = parameters.TryGetPropertyValue("name", out var tn) ? tn?.GetValue<string>() ?? "" : "";
                        var toolArgs = parameters.TryGetPropertyValue("arguments", out var ta) ? ta as JsonObject : null;
                        result = await _server.HandleToolCallAsync(toolName, toolArgs, ct);
                    }
                    break;

                case "create_window":
                    var title = parameters?.TryGetPropertyValue("title", out var t) == true ? t?.GetValue<string>() ?? "Window" : "Window";
                    var w = parameters?.TryGetPropertyValue("width", out var wid) == true ? wid?.GetValue<int>() ?? 800 : 800;
                    var h = parameters?.TryGetPropertyValue("height", out var ht) == true ? ht?.GetValue<int>() ?? 600 : 600;
                    result = await _server.CreateWindowAsync(title, w, h);
                    break;

                case "set_window_layout":
                    var layout = parameters?.TryGetPropertyValue("layout", out var lo) == true ? lo as JsonObject : null;
                    result = await _server.SetWindowLayoutAsync("main", layout);
                    break;

                case "add_drop_zone":
                    var dzId = parameters?.TryGetPropertyValue("zone_id", out var dzi) == true ? dzi?.GetValue<string>() ?? "" : "";
                    var dzLabel = parameters?.TryGetPropertyValue("label", out var dzlab) == true ? dzlab?.GetValue<string>() ?? "" : "";
                    result = await _server.AddDropZoneAsync("main", dzId, dzLabel);
                    break;

                case "update_file_list":
                    var files = parameters?.TryGetPropertyValue("files", out var fl) == true ? fl as JsonArray : null;
                    result = await _server.UpdateFileListAsync("main", files);
                    break;

                case "add_button":
                    var btnId = parameters?.TryGetPropertyValue("button_id", out var bi) == true ? bi?.GetValue<string>() ?? "" : "";
                    var btnLabel = parameters?.TryGetPropertyValue("label", out var bl) == true ? bl?.GetValue<string>() ?? "" : "";
                    var btnIcon = parameters?.TryGetPropertyValue("icon", out var bicon) == true ? bicon?.GetValue<string>() ?? "" : "";
                    result = await _server.AddButtonAsync("main", btnId, btnLabel, btnIcon);
                    break;

                case "add_label":
                    var lblId = parameters?.TryGetPropertyValue("label_id", out var li) == true ? li?.GetValue<string>() ?? "" : "";
                    var lblText = parameters?.TryGetPropertyValue("text", out var lt) == true ? lt?.GetValue<string>() ?? "" : "";
                    result = await _server.AddLabelAsync("main", lblId, lblText);
                    break;

                case "set_status_text":
                    var stText = parameters?.TryGetPropertyValue("text", out var stt) == true ? stt?.GetValue<string>() ?? "" : "";
                    result = await _server.SetStatusTextAsync("main", stText);
                    break;

                case "set_progress":
                    var pct = parameters?.TryGetPropertyValue("percent", out var pc) == true ? pc?.GetValue<int>() ?? 0 : 0;
                    var pctLabel = parameters?.TryGetPropertyValue("label", out var pcl) == true ? pcl?.GetValue<string>() ?? "" : "";
                    result = await _server.SetProgressAsync("main", pct, pctLabel);
                    break;

                case "register_callback":
                    var cbEvent = parameters?.TryGetPropertyValue("event_type", out var cbe) == true ? cbe?.GetValue<string>() ?? "" : "";
                    var cbId = parameters?.TryGetPropertyValue("callback_id", out var cbid) == true ? cbid?.GetValue<string>() ?? "" : "";
                    result = await _server.RegisterCallbackAsync("main", cbEvent, cbId);
                    break;

                case "poll_events":
                    result = await _server.PollEventsAsync("main");
                    break;

                case "clear_window":
                    result = await _server.ClearWindowAsync("main");
                    break;

                case "close_window":
                    result = await _server.CloseWindowAsync("main");
                    break;

                default:
                    result = new JsonObject { ["error"] = $"Unknown method: {method}" };
                    break;
            }

            var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonValue.Create(id) };
            if (result != null)
                response["result"] = JsonNode.Parse(JsonSerializer.Serialize(result));

            return response.ToJsonString();
        }

        private JsonNode[] GetToolDefs()
        {
            return new JsonNode[]
            {
                MakeTool("create_window", "Create a new WinUI window",
                    new[] { ("title", "string", "Window title"), ("width", "integer", "Width"), ("height", "integer", "Height") }),
                MakeTool("add_drop_zone", "Add a file drop zone",
                    new[] { ("window_id", "string", ""), ("zone_id", "string", ""), ("label", "string", "") }),
                MakeTool("update_file_list", "Update the file list display",
                    new[] { ("window_id", "string", ""), ("files", "array", "") }),
                MakeTool("add_button", "Add a button",
                    new[] { ("window_id", "string", ""), ("button_id", "string", ""), ("label", "string", ""), ("icon", "string", "") }),
                MakeTool("add_label", "Add a text label",
                    new[] { ("window_id", "string", ""), ("label_id", "string", ""), ("text", "string", "") }),
                MakeTool("set_status_text", "Update status bar",
                    new[] { ("window_id", "string", ""), ("text", "string", "") }),
                MakeTool("set_progress", "Update progress bar",
                    new[] { ("window_id", "string", ""), ("percent", "integer", ""), ("label", "string", "") }),
                MakeTool("register_callback", "Register UI event callback",
                    new[] { ("window_id", "string", ""), ("event_type", "string", ""), ("callback_id", "string", "") }),
                MakeTool("poll_events", "Poll pending UI events",
                    new[] { ("window_id", "string", "") }),
                MakeTool("clear_window", "Clear window",
                    new[] { ("window_id", "string", "") }),
                MakeTool("close_window", "Close window",
                    new[] { ("window_id", "string", "") }),
            };
        }

        private JsonNode MakeTool(string name, string desc, (string pname, string ptype, string pdesc)[] props)
        {
            var propsObj = new JsonObject();
            foreach (var p in props)
                propsObj[p.pname] = JsonNode.Parse(JsonSerializer.Serialize(new { type = p.ptype, description = p.pdesc }));

            var required = props.Where(p => p.pname != "window_id").Select(p => JsonValue.Create(p.pname)).ToArray();

            return JsonNode.Parse(JsonSerializer.Serialize(new
            {
                name,
                description = desc,
                inputSchema = new
                {
                    type = "object",
                    properties = propsObj,
                    required
                }
            }))!;
        }
    }
}
