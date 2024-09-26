using Discord;
using Discord.WebSocket;
using OpenAI.Chat;

namespace AiHelpBot;

public class AiClient
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
                ? new AssistantChatMessage(SystemMessage)
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
    }

    public async Task<string> CompleteChatAsync(SocketMessage message, CancellationToken cancellationToken = default)
    {
        _chatMessages.Add(new UserChatMessage(message.Content));

        bool requiresAction;
        do
        {
            requiresAction = false;
            ChatCompletion completion = await _chatClient.CompleteChatAsync(MessagesWithSystem,
                new ChatCompletionOptions()
                {
                    Tools =
                    {
                        ChatTool.CreateFunctionTool(
                            functionName: nameof(AddReactionAsync),
                            functionDescription: "Add a heart reaction to the message."
                        )
                    }
                }, cancellationToken);

            switch (completion.FinishReason)
            {
                case ChatFinishReason.Stop:
                {
                    _chatMessages.Add(new AssistantChatMessage(completion));
                    return completion.ToString();
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
                                catch (Exception)
                                {
                                    _chatMessages.Add(new ToolChatMessage(toolCall.Id, "Failed to add reaction."));
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

    private static readonly string SystemMessage = """
                                                   You are an assistant, tasked with helping users troubleshooting their installation of Jellyfin on their Samsung Tizen TVs.
                                                   # jellyfin-tizen-builds
                                                   The purpose of this project is to automatically build the most up-to-date version of jellyfin-tizen.

                                                   ## Versions
                                                   | File name    | Description                                                                                                               |
                                                   |--------------|---------------------------------------------------------------------------------------------------------------------------|
                                                   | Jellyfin.wgt | Built with the latest stable release of jellyfin-web                                                                      |
                                                   | 10.9.z       | Built with the bleeding edge of the branch for the 10.9.z releases                                                        |
                                                   | 10.8.z       | Built with the bleeding edge of the branch for the 10.8.z releases                                                        |
                                                   | master       | Built with the latest potentially unstable changes to jellyfin-web code (this will always be the newest possible version) |
                                                   | TrueHD       | TrueHD support is enabled (whether it works or not might depend on TV model)                                              |
                                                   | intros       | Built with the modified web interface for https://github.com/jumoog/intro-skipper (might work)                            |
                                                   | secondary    | Built with the latest stable release of jellyfin-web and a different app ID to allow having a second account signed in    |

                                                   *Disclaimer: I don't have many success stories with TVs older than 2018, but a few people in my Discord server have reported it working for their 2015 and 2016 TVs with the `10.8.z` version*

                                                   The latest release can be found at https://github.com/jeppevinkel/jellyfin-tizen-builds/releases/latest

                                                   ## Installation
                                                   For a one step install process using Docker, check out this guide made by Georift [Georift/install-jellyfin-tizen](https://github.com/Georift/install-jellyfin-tizen).  
                                                   *I have no affiliation with Georift and I can't provide support related to that project since I have not personally used it or helped in its creation.*

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

                                                   Keep message short and to the point. No huge paragraphs unless explicitly requested.
                                                   """;
}