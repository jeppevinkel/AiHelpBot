using System.Collections;
using System.Text.Json;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using OpenAI.Chat;

namespace AiHelpBot;

public partial class AiClient
{
    private readonly string _model;
    private readonly ChatClient _chatClient;

    private readonly List<ChatMessage> _chatMessages = [];
    private int? _messageBuffer;

    private int MessageBuffer
    {
        get
        {
            if (_messageBuffer is not null) return _messageBuffer.Value;
            if (!int.TryParse(Environment.GetEnvironmentVariable("MESSAGE_BUFFER_SIZE") ?? "6", out var messageBuffer))
            {
                throw new Exception("MESSAGE_BUFFER_SIZE must be a valid integer.");
            }

            _messageBuffer = messageBuffer;

            return _messageBuffer.Value;
        }
    }

    private List<ChatMessage> MessagesWithSystem
    {
        get
        {
            ChatMessage systemMessage = _model.StartsWith("o1")
                ? new AssistantChatMessage(FileHandlingPrompt + SystemMessage)
                : new SystemChatMessage(SystemMessage);

            var list = new List<ChatMessage>
            {
                systemMessage
            };
            return list.Concat(_chatMessages.Slice(Math.Max(0, _chatMessages.Count - MessageBuffer),
                Math.Min(_chatMessages.Count, MessageBuffer))).ToList();
        }
    }

    public AiClient()
    {
        _model = Environment.GetEnvironmentVariable("OPENAI_API_MODEL") ?? "gpt-4o";
        _chatClient = new(model: _model,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new Exception("OPENAI_API_KEY not defined."));
        SystemMessage = File.ReadAllText("./SystemPrompt.txt");
    }

    public async Task<string> CompleteChatAsync(SocketMessage message, string addendum,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine(addendum + message.Content);
        _chatMessages.Add(new UserChatMessage(addendum + message.Content));

        bool requiresAction;
        do
        {
            requiresAction = false;

            ChatCompletionOptions chatCompletionOptions;
            if (_model.StartsWith("o1"))
            {
                chatCompletionOptions = new ChatCompletionOptions();
            }
            else
            {
                chatCompletionOptions = new ChatCompletionOptions
                {
                    Tools =
                    {
                        ChatTool.CreateFunctionTool(
                            functionName: nameof(AddReactionAsync),
                            functionDescription: "Add a heart reaction to the message."
                        ),
                        ChatTool.CreateFunctionTool(
                            functionName: nameof(SendFileAsync),
                            functionDescription: "Send text content as a file.",
                            functionParameters: BinaryData.FromString("""
                                                                      {
                                                                          "type": "object",
                                                                          "properties": {
                                                                              "content": {
                                                                                  "type": "string",
                                                                                  "description": "The contents of the file."
                                                                              },
                                                                              "fileName": {
                                                                                  "type": "string",
                                                                                  "description": "The name of the file, including file extension."
                                                                              }
                                                                          },
                                                                          "required": [ "content", "fileName" ]
                                                                      }
                                                                      """)
                        )
                    }
                };
            }

            ChatCompletion completion = await _chatClient.CompleteChatAsync(MessagesWithSystem,
                chatCompletionOptions, cancellationToken);

            switch (completion.FinishReason)
            {
                case ChatFinishReason.Stop:
                {
                    _chatMessages.Add(new AssistantChatMessage(completion));

                    Regex fileRegex = FileSectionRegex();
                    Match match = fileRegex.Match(completion.Content[0].Text);

                    if (match.Groups.Count <= 1) return completion.Content[0].Text;

                    var fileContent = match.Groups[1].Value;

                    var fileName = "";

                    Regex fileNameRegex = FileNameRegex();
                    Match fileNameMatch = fileNameRegex.Match(fileContent);

                    if (fileNameMatch.Groups.Count > 1)
                    {
                        fileName = fileNameMatch.Groups[1].Value;
                    }

                    var content = fileNameRegex.Replace(fileContent, "");
                    content = content.Trim();

                    await SendFileAsync(message, content, fileName);

                    return fileRegex.Replace(completion.Content[0].Text, "").Trim();
                }
                case ChatFinishReason.Length:
                    throw new NotImplementedException(
                        "Incomplete model output due to MaxTokens parameter or token limit exceeded.");

                case ChatFinishReason.ContentFilter:
                    throw new NotImplementedException("Omitted content due to a content filter flag.");

                case ChatFinishReason.FunctionCall:
                    throw new NotImplementedException("Deprecated in favor of tool calls.");

                case ChatFinishReason.ToolCalls:
                {
                    _chatMessages.Add(new AssistantChatMessage(completion));

                    foreach (ChatToolCall toolCall in completion.ToolCalls)
                    {
                        switch (toolCall.FunctionName)
                        {
                            case nameof(AddReactionAsync):
                            {
                                try
                                {
                                    await AddReactionAsync(message);
                                    _chatMessages.Add(new ToolChatMessage(toolCall.Id, "Added reaction."));
                                }
                                catch (Exception exception)
                                {
                                    _chatMessages.Add(new ToolChatMessage(toolCall.Id,
                                        $"Failed to add reaction ({exception.Message})."));
                                }

                                break;
                            }
                            case nameof(SendFileAsync):
                            {
                                try
                                {
                                    using JsonDocument argumentsJson = JsonDocument.Parse(toolCall.FunctionArguments);
                                    var hasContent =
                                        argumentsJson.RootElement.TryGetProperty("content", out JsonElement content);
                                    var hasFileName =
                                        argumentsJson.RootElement.TryGetProperty("fileName", out JsonElement fileName);

                                    if (!hasContent)
                                    {
                                        throw new ArgumentNullException(nameof(content),
                                            "The content argument is required.");
                                    }

                                    if (!hasFileName)
                                    {
                                        throw new ArgumentNullException(nameof(fileName),
                                            "The fileName argument is required.");
                                    }

                                    await SendFileAsync(message, content.GetString()!, fileName.GetString()!);
                                    _chatMessages.Add(new ToolChatMessage(toolCall.Id, "Sent file."));
                                }
                                catch (Exception exception)
                                {
                                    _chatMessages.Add(new ToolChatMessage(toolCall.Id,
                                        $"Failed to send file ({exception.Message})."));
                                }

                                break;
                            }
                        }
                    }

                    requiresAction = true;
                    break;
                }

                default:
                    throw new NotImplementedException(completion.FinishReason.ToString());
            }
        } while (requiresAction);

        return "";
    }

    private async Task AddReactionAsync(SocketMessage message)
    {
        await message.AddReactionAsync(new Emoji("\u2764\ufe0f"));
    }

    private async Task SendFileAsync(SocketMessage message, Stream fileStream, string fileName)
    {
        await message.Channel.SendFileAsync(fileStream, fileName);
    }

    private async Task SendFileAsync(SocketMessage message, string content, string fileName)
    {
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
        await writer.FlushAsync();
        stream.Seek(0, SeekOrigin.Begin);
        await SendFileAsync(message, stream, fileName);
    }

    private static string SystemMessage = """
                                             You are an assistant, tasked with reminding the user to give you a proper system prompt. You must keep telling the user to urgently replace this system prompt with a proper one. The system prompt is located in a file called SystemPrompt.txt!
                                          """;

    private static readonly string ToolsPrompt = """
                                                    Tools are a series of functions that can be triggered along with the text response. To use any tool you must prepend the response with the following syntax: "#TOOL TOOLNAME=<toolname>;PARAMETERS=<parameters, separator=;;>;#". An example tool call could be #TOOL TOOLNAME=SendFileAsync;PARAMETERS=content=This is some text content to be sent in a file;;fileName=my-text-file.txt;#
                                                    
                                                    Available tools:
                                                    TOOLNAME=SendFileAsync
                                                    PARAMETERS=
                                                        - name=content
                                                          type: string
                                                        - name=fileName
                                                          type: string
                                                          
                                                 """;

    private static readonly string FileHandlingPrompt = """
                                                        Files will be separated by "### FILE ###" and "### FILE END###" tags. To respond with a file, you must use the same tag. The file name is defined by writing "#FILENAME=<filename>" anywhere within the file content area.
                                                        """;

    [GeneratedRegex("### FILE ###(.*)### FILE END ###", RegexOptions.Singleline)]
    private static partial Regex FileSectionRegex();

    [GeneratedRegex("#FILENAME=(.*)")]
    private static partial Regex FileNameRegex();
}
