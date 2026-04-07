using LlmChamber;

Console.WriteLine("=== LlmChamber コンソールサンプル ===");
Console.WriteLine();

// LlmChamberインスタンスを作成（非DIパターン）
await using var llm = LlmChamberFactory.Create(options =>
{
    options.DefaultModel = "gemma4-e2b";
    options.RuntimeVariant = RuntimeVariant.Auto;
});

// ダウンロード進捗表示
llm.RuntimeDownloadProgress += (_, p) =>
{
    if (p.Percentage.HasValue)
        Console.Write($"\rランタイムDL: {p.Percentage:F1}% ({p.StatusMessage})      ");
    else
        Console.Write($"\r{p.StatusMessage}      ");
};

llm.ModelDownloadProgress += (_, p) =>
{
    if (p.Percentage.HasValue)
        Console.Write($"\rモデルDL: {p.Percentage:F1}% ({p.StatusMessage})      ");
    else
        Console.Write($"\r{p.StatusMessage}      ");
};

// 初期化（ランタイムDL + モデルpull）
Console.WriteLine("初期化中...");
await llm.InitializeAsync();
Console.WriteLine();
Console.WriteLine("準備完了！チャットを開始します。'exit' で終了。");
Console.WriteLine();

// チャットセッション
var session = llm.CreateChatSession(new ChatOptions
{
    SystemPrompt = "あなたは親切で簡潔に回答するアシスタントです。日本語で回答してください。",
});

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("You> ");
    Console.ResetColor();

    string? input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("AI> ");
    Console.ResetColor();

    // ストリーミング応答
    await foreach (string chunk in session.SendAsync(input))
    {
        Console.Write(chunk);
    }

    Console.WriteLine();
    Console.WriteLine();
}

Console.WriteLine("終了します。");
