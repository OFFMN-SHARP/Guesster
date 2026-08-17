using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.Text;
using System.Windows.Forms;
using System.Text.RegularExpressions;

int UiWait = 600;
int Wait = 300;
string GetApiKey()
{
    Console.Write("请输入API Key（或按回车使用环境变量）: ");
    var input = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(input)) return input;
    return Environment.GetEnvironmentVariable("OPENAI_API_KEY")
           ?? throw new Exception("未找到API Key，请设置环境变量或手动输入。");
}
var apiKey = args.Length > 0 ? args[0] : GetApiKey();
var client = new OpenAIClient(apiKey);
var modelClient = client.GetOpenAIModelClient();
var modelsResult = await modelClient.GetModelsAsync();
var chatModels = modelsResult.Value
    .Where(m => m.Id.StartsWith("gpt-") || m.Id.StartsWith("claude-") || m.Id.Contains("chat"))
    .ToList();
if (chatModels.Count==0) throw new Exception("没有找到可用的聊天模型。");
string selectedModel = SelectModel(chatModels.Select(m => m.Id).ToList());
IChatClient chatClient = new ChatClient(model: selectedModel, apiKey: apiKey).AsIChatClient();
string SelectModel(List<string> modelList)
{
    int index = 0;
    Console.Clear();
    Console.WriteLine("请使用 ↑/↓ 选择模型，按 Enter 确认：\n");
    while (true)
    {
        for (int i = 0; i < modelList.Count; i++)
        {
            var prefix = (i == index) ? ">> " : "   ";
            Console.SetCursorPosition(0, 2 + i);
            Console.Write($"{prefix}{modelList[i]}");
        }
        var key = Console.ReadKey(true).Key;
        switch (key)
        {
            case ConsoleKey.UpArrow:
                index = (index - 1 + modelList.Count) % modelList.Count;
                break;
            case ConsoleKey.DownArrow:
                index = (index + 1) % modelList.Count;
                break;
            case ConsoleKey.Enter:
                return modelList[index];
        }
    }
}
string systemPrompt = """
你是一个C#代码生成器。用户会描述一个任务，你只需要返回可直接执行的C#代码。
- 不要包含任何解释、注释、Markdown标记
- 不要包含namespace、class、Main方法
- 直接写可执行的语句，用Console.WriteLine输出结果
- 如果任务无法完成，用throw new Exception("原因")抛出异常
""";
var messages = new List<Microsoft.Extensions.AI.ChatMessage>
{new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.System, systemPrompt)};

var ilRunTool = AIFunctionFactory.Create(
    async (string code) =>
    {
        Console.WriteLine($"[工具执行] 正在执行代码...");
        var result = await ExecuteCode(code);
        return result;
    },
    new AIFunctionFactoryOptions
    {
        Name = "il_run",
        Description = "执行一段 C# 代码，并返回执行结果或错误信息。"
    }
);
var options = new ChatOptions
{
    Tools = [ilRunTool] // 把工具塞给 AI
};

async Task<string> ExecuteCode(string code)
{
    // 1. 检测是否为阻塞 UI 的代码
    bool hasInteractiveUI = code.Contains("ShowDialog") ||
                            code.Contains("Show") ||  // 针对 WinForms 和 WPF
                            code.Contains("Application.Run") ||
                            code.Contains("MessageBox.Show");

    // 2. 设定超时时间：UI 交互给 10 分钟，普通代码给 5 秒
    TimeSpan timeout = hasInteractiveUI ? TimeSpan.FromSeconds(UiWait) : TimeSpan.FromSeconds(Wait);

    using var cts = new CancellationTokenSource(timeout);
    try
    {
        var scriptOptions = ScriptOptions.Default
            .WithImports("System", "System.IO", "System.Collections.Generic", "System.Linq", "System.Text", "System.Windows.Forms")
            .WithReferences(
                typeof(object).Assembly,
                typeof(Console).Assembly,
                typeof(Enumerable).Assembly,
                typeof(StringBuilder).Assembly,
                typeof(Form).Assembly
            );

        var result = await CSharpScript.RunAsync(code, scriptOptions, cancellationToken: cts.Token);
        return result.ReturnValue?.ToString() ?? "执行成功（无输出）";
    }
    catch (CompilationErrorException ex)
    {
        return $"编译错误:\n{string.Join("\n", ex.Diagnostics)}";
    }
    catch (OperationCanceledException)
    {
        // 根据类型返回友好提示
        return hasInteractiveUI
            ? "UI 操作超时（超过10分钟），已终止。"
            : "执行超时（超过5秒），已终止。";
    }
    catch (Exception ex)
    {
        return $"运行时错误: {ex.Message}";
    }
}
bool Ask()
{
    Console.WriteLine(" 是否运行? (← 否 / → 是) : ");
    bool result = false;
    while (true)
    {
        var key = Console.ReadKey(true).Key;
        if (key == ConsoleKey.LeftArrow) { result = false; Console.Write("\r  否 "); }
        else if (key == ConsoleKey.RightArrow) { result = true; Console.Write("\r  是 "); }
        else if (key == ConsoleKey.Enter) return result;
    }
}
while (true)
{
    Console.Write($"\n[用户:{Environment.UserName}] > ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.ToLower() == "/q") break;
    if (input.ToLower() == "/clear")
    {
        messages.Clear();
        messages.Add(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.System, systemPrompt));
        Console.WriteLine("上下文已清空。");
        continue;
    }
    if (input.StartsWith("/timeout"))
    {
        var parts = input.Split(' ');
        if (parts.Length > 1 && int.TryParse(parts[1], out int seconds))
        {
            Wait=seconds;
            UiWait = seconds * 4;
            Console.WriteLine($"⏱️ 超时时间已设置为 {seconds} 秒");
        }
        continue;
    }
    // 添加用户消息
    messages.Add(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, input));

    // 调用 AI，传入 tools
    var response = await chatClient.GetResponseAsync(messages, options);
    var assistantMessage = response.Messages.FirstOrDefault();
    if (assistantMessage == null) continue;

    // 检查是否有工具调用请求
    bool hasToolCalls = assistantMessage.Contents.Any(c => c is Microsoft.Extensions.AI.FunctionCallContent);

    if (!hasToolCalls)
    {
        // 纯文本回复
        var text = assistantMessage.Text ?? string.Empty;
        Console.WriteLine($"[AI] {text}");
        messages.Add(assistantMessage);
    }
    else
    {
        // 有工具调用请求
        messages.Add(assistantMessage);

        var toolResults = new List<Microsoft.Extensions.AI.ChatMessage>();
        foreach (var content in assistantMessage.Contents.OfType<Microsoft.Extensions.AI.FunctionCallContent>())
        {
            Console.WriteLine($"[AI 请求] 调用工具: {content.Name}");

            // 安全提取参数 code（避免命名冲突，改用 funcArgs）
            var funcArgs = content.Arguments;
            string code = funcArgs != null && funcArgs.TryGetValue("code", out var codeObj)
                ? codeObj?.ToString() ?? string.Empty
                : string.Empty;

            Console.WriteLine($"[生成的代码]\n{code}");
            Console.Write("是否运行?(方向键选择): ");
            if (!Ask())
            {
                var cancelResult = "用户取消了代码执行。";
                toolResults.Add(new Microsoft.Extensions.AI.ChatMessage(
                    Microsoft.Extensions.AI.ChatRole.Tool,
                    new Microsoft.Extensions.AI.AIContent[] { new Microsoft.Extensions.AI.FunctionResultContent(content.CallId, cancelResult) }
                ));
                continue;
            }

            var execResult = await ExecuteCode(code);
            Console.WriteLine($"[执行结果]\n{execResult}");
            toolResults.Add(new Microsoft.Extensions.AI.ChatMessage(
                Microsoft.Extensions.AI.ChatRole.Tool,
                new Microsoft.Extensions.AI.AIContent[] { new Microsoft.Extensions.AI.FunctionResultContent(content.CallId, execResult) }
            ));
        }

        foreach (var toolMsg in toolResults)
            messages.Add(toolMsg);

        Console.WriteLine("[AI] 总结中...");
        var finalResponse = await chatClient.GetResponseAsync(messages, options);
        var finalMessage = finalResponse.Messages.FirstOrDefault();
        if (finalMessage != null)
        {
            var finalText = finalMessage.Text ?? string.Empty;
            Console.WriteLine($"[AI] {finalText}");
            messages.Add(finalMessage);
        }
    }
}