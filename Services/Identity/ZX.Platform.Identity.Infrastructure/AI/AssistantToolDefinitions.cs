using System.Text.Json;

namespace ZX.Platform.Identity.Infrastructure.AI;

internal static class AssistantToolDefinitions
{
    public static readonly JsonElement Value = JsonSerializer.Deserialize<JsonElement>("""
        [
          {
            "type": "function",
            "function": {
              "name": "search_project_sources",
              "description": "搜索本版本已发布的前后端业务源码。系统功能、操作步骤和字段问题先搜索这里。用中英文界面词、类名或组件名搜索，返回带行号的相关片段。无数据库访问。",
              "parameters": {
                "type": "object",
                "properties": { "query": { "type": "string", "description": "1到200字的搜索词，例如：菜单 新增 MenuEditorDialog createMenu" } },
                "required": ["query"],
                "additionalProperties": false
              }
            }
          },
          {
            "type": "function",
            "function": {
              "name": "read_project_source",
              "description": "阅读源码文件的指定行；必要时继续读下一段或语言词条。path只能使用源码搜索或查询目录提供的相对路径，不能访问其他文件。",
              "parameters": {
                "type": "object",
                "properties": {
                  "path": { "type": "string" },
                  "startLine": { "type": "integer", "minimum": 1 },
                  "lineCount": { "type": "integer", "minimum": 1, "maximum": 120 }
                },
                "required": ["path", "startLine", "lineCount"],
                "additionalProperties": false
              }
            }
          },
          {
            "type": "function",
            "function": {
              "name": "get_my_permissions",
              "description": "获取当前登录用户的实际功能权限，核实为什么按钮或模块不可用。只查询当前账号，不接收账号ID。源码操作说明通常不需要调用。",
              "parameters": { "type": "object", "properties": {}, "additionalProperties": false }
            }
          },
          {
            "type": "function",
            "function": {
              "name": "list_available_queries",
              "description": "分页发现当前账号获准使用的已标记只读接口，取得ID、参数、结果形状和源码位置，再执行。每页最多8条，hasMore时用nextOffset继续且保持query相同；默认offset=0。中文搜不到时不传query分页浏览，或先搜索源码取得英文Controller名再搜。无描述时结合源码及返回类型理解，源码路径可能被发布资料过滤。",
              "parameters": {
                "type": "object",
                "properties": {
                  "query": { "type": "string", "description": "可选的模块、接口或业务搜索词，最多200字；未知接口时可留空先浏览目录" },
                  "offset": { "type": "integer", "minimum": 0, "description": "首批用0或省略，后续使用上次nextOffset" }
                },
                "additionalProperties": false
              }
            }
          },
          {
            "type": "function",
            "function": {
              "name": "execute_query",
              "description": "使用目录ID和允许参数执行只读查询，仍检查实际调用者权限及SBU。不能传入URL、SQL、HTTP方法、调用者身份或请求头。parameters只放原接口参数，无参数时传{}；目录里的userId/id等只表示业务查询对象，不能替换调用者。filters可选，只筛平面列表顶层标量：默认eq，或{eq:标量}/{contains:文本}。complete_tree查询不传filters，应递归读取children。字段语义先查资料，不猜ID或字段。结果过大或分页未完整时不能计算全局最大值、合计或排名。",
              "parameters": {
                "type": "object",
                "properties": {
                  "id": { "type": "string", "description": "list_available_queries返回的准确接口ID，最多200字" },
                  "parameters": { "type": "object", "description": "目录列出的查询/路径参数；无参数接口传{}" },
                  "filters": {
                    "type": "object",
                    "description": "可选的结果行筛选，最多20字段；标量值默认eq，也可用{eq:值}或{contains:文本}。只用于完整列表的顶层标量列，不接受数组、其它操作或表达式",
                    "maxProperties": 20,
                    "additionalProperties": {
                      "anyOf": [
                        { "type": ["string", "number", "boolean", "null"] },
                        {
                          "type": "object",
                          "properties": {
                            "eq": { "type": ["string", "number", "boolean", "null"] },
                            "contains": { "type": "string", "minLength": 1 }
                          },
                          "minProperties": 1,
                          "maxProperties": 1,
                          "additionalProperties": false
                        }
                      ]
                    }
                  }
                },
                "required": ["id", "parameters"],
                "additionalProperties": false
              }
            }
          }
        ]
        """);
}
