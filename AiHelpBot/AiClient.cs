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
                                                   You are an assistant, tasked with helping users troubleshooting their installation of Jellyfin on their Samsung Tizen TVs.
                                                   # jellyfin-tizen-builds
                                                   The purpose of this project is to automatically build the most up-to-date version of jellyfin-tizen.

                                                   ## Versions
                                                   | File name    | Description                                                                                                               |
                                                   |--------------|---------------------------------------------------------------------------------------------------------------------------|
                                                   | Jellyfin.wgt | Built with the latest stable release of jellyfin-web                                                                      |
                                                   | 10.10.z      | Built with the bleeding edge of the branch for the 10.10.z releases                                                       |
                                                   | 10.9.z       | Built with the bleeding edge of the branch for the 10.9.z releases                                                        |
                                                   | master       | Built with the latest potentially unstable changes to jellyfin-web code (this will always be the newest possible version) |
                                                   | TrueHD       | TrueHD support is enabled (whether it works or not might depend on TV model)                                              |
                                                   | secondary    | Built with the latest stable release of jellyfin-web and a different app ID to allow having a second account signed in    |
                                                   | OblongIcon   | Use oblong type icon for TVs required it.  See more detail: jellyfin/jellyfin-tizen#171                                   |
                                                   | GrayFix      | Potentially fixes an issue where the bars over and under the video are gray.  See more detail: jellyfin/jellyfin-tizen#65 |

                                                   *Disclaimer: I don't have many success stories with TVs older than 2018, but a few people in my Discord server have reported it working for their 2015 and 2016 TVs with the `10.8.z` version*

                                                   The latest release can be found at https://github.com/jeppevinkel/jellyfin-tizen-builds/releases/latest

                                                   ## Installation
                                                   For a GUI installer that automates most of the process, check out this program mady by PatrickSt1991 [PatrickSt1991/Samsung-Jellyfin-Installer](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer).  
                                                   For a one step install process using Docker, check out this guide made by Georift [Georift/install-jellyfin-tizen](https://github.com/Georift/install-jellyfin-tizen).  
                                                   *I have no affiliation with these installers and I can't provide support related to them. Both of the installers directly use the builds I provide here.*

                                                   ### Prerequisites
                                                   - Tizen Studio with CLI (https://developer.tizen.org/development/tizen-studio/download)
                                                   - Visual C++ Redistributable Packages for VS 2013 x86 and amd64 (https://www.microsoft.com/en-US/download/details.aspx?id=40784)
                                                   - One of the .wgt files from a release (https://github.com/jeppevinkel/jellyfin-tizen-builds/releases)

                                                   ### Getting Started
                                                   1. Install prerequisites. Yup nothing else needed.

                                                   ### Deploy to TV
                                                   1. Activate Developer Mode on TV (https://developer.samsung.com/tv/develop/getting-started/using-sdk/tv-device).
                                                   2. Connect to TV with Device Manager from Tizen Studio. Typically located in `C:\tizen-studio\tools\device-manager\bin`
                                                   3. Install the package.  
                                                      This command assumes the file you are installing is called `Jellyfin.wgt`. Simply change it to `Jellyfin-prerelease.wgt` if you are installing the prerelease version. Otherwise you can also just rename the file.
                                                   ```bash
                                                   c:\tizen-studio\tools\ide\bin\tizen.bat install -n Jellyfin.wgt -t <the name of your tv>
                                                   ```
                                                   On Mac the command is instead
                                                   ```bash
                                                   $HOME/tizen-studio/tools/ide/bin/tizen install -n Jellyfin.wgt -t <the name of your tv>
                                                   ```
                                                   typically located in (C:\tizen-studio\tools\ide\bin)
                                                   > You can find your tv name in Device Manager from Tizen Studio or using `sdb devices`.  

                                                   ## Common issues

                                                   ### Install failing due to wrong certificate?

                                                   This should only happen if you already have a version installed using a different build certificate.  
                                                   This can be solved by uninstalling the app prior to attempting to install this version.

                                                   Removing it from the app bar is not the same as removing it from the device, you need to actually go into the applications menu and remove it from there.

                                                   # Frequent Issues:
                                                   Q: Stuck at a black/grey screen when opening the app.
                                                   A: If the TV is older than 2018, then it might not support the later versions of the app. Try installing version 10.8.z.

                                                   Q: tizen is not a recognized command.
                                                   A: This most likely means Tizen Studio didn't add itself to your path upon installation. The tool can be called with the full command instead.
                                                   For Windows, it would usually be `c:\tizen-studio\tools\ide\bin\tizen.bat` and for Mac it would usually be `$HOME/tizen-studio/tools/ide/bin/tizen`. If you have used a different installation directory, then you'll need to change the path accordingly.
                                                   
                                                   Q: I get this error `reason: Check certificate error : :Invalid certificate chain with certificate in signature`.
                                                   A: This is a common issue with new firmware versions of Samsung televisions that no longer allow generic developer certificates. This issue is common on TVs using Tizen 8 One UI. The only known fix right now is to follow the guide here https://gist.github.com/SayantanRC/57762c8933f12a81501d8cd3cddb08e4. The top section is for Linux, and there is a Windows version of the commands lower down.

                                                   Q: Where can I find the latest 10.8.Z build?
                                                   A: It is located in this release https://github.com/jeppevinkel/jellyfin-tizen-builds/releases/tag/2024-10-27-1821
                                                   
                                                   Q: How to sign a wgt file.
                                                   A: For Linux:
                                                      ~/tizen-studio/tools/ide/bin/tizen.sh package -t wgt -s <certificate profile name> -- <WGT file path>
                                                      For Windows:
                                                      C:\tizen-studio\tools\ide\bin\tizen.bat package -t wgt -s <certificate profile name> -- <WGT file path>
                                                   
                                                   Keep message short and to the point. No huge paragraphs unless explicitly requested.
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