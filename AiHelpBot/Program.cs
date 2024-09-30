using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace AiHelpBot;

class Program
{
    private static readonly DiscordSocketClient DiscordSocketClient = new(new DiscordSocketConfig()
    {
        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
    });
    private static readonly AiClient AiClient = new();
    private static ulong _channelId;

    static async Task Main(string[] args)
    {
        try
        {
            if (!ulong.TryParse(Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID"), out _channelId))
            {
                throw new Exception("DISCORD_CHANNEL_ID is invalid.");
            }

            DiscordSocketClient.Log += LogAsync;

            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ??
                        throw new Exception("DISCORD_BOT_TOKEN not defined.");

            await DiscordSocketClient.LoginAsync(TokenType.Bot, token);
            await DiscordSocketClient.StartAsync();

            DiscordSocketClient.MessageReceived += MessageReceived;

            await Task.Delay(-1);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private static async Task MessageReceived(SocketMessage message)
    {
        if (message.Author.IsBot)
        {
            return;
        }
        if (message.Channel.Id == _channelId && (message.MentionedUsers.Count < 0 || message.MentionedUsers.Any(it => it.Id == DiscordSocketClient.CurrentUser.Id)))
        {
            var aiResponse = await AiClient.CompleteChatAsync(message);
            var responseMessages = aiResponse.SplitByLength(2000, "\n");

            foreach (var responseMessage in responseMessages)
            {
                await message.Channel.SendMessageAsync(responseMessage);
            }
        }
    }

    private static Task LogAsync(LogMessage logMessage)
    {
        if (logMessage.Exception is CommandException cmdException)
        {
            Console.WriteLine($"[Command/{logMessage.Severity}] {cmdException.Command.Aliases[0]}"
                              + $" failed to execute in {cmdException.Context.Channel}.");
            Console.WriteLine(cmdException);
        }
        else
        {
            Console.WriteLine($"[General/{logMessage.Severity}] {logMessage}");
        }

        return Task.CompletedTask;
    }
}