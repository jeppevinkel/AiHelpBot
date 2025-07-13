using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace AiHelpBot.Services;

public class DiscordBotService : BackgroundService
{
    private readonly DiscordSocketClient _discordSocketClient;
    private readonly AiClient _aiClient;
    private ulong _channelId;
    private const int FileLengthLimit = 30000;
    private readonly ILogger<DiscordBotService> _logger;

    public DiscordBotService(ILogger<DiscordBotService> logger)
    {
        _logger = logger;
        _discordSocketClient = new DiscordSocketClient(new DiscordSocketConfig()
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        });
        _aiClient = new AiClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!ulong.TryParse(Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID"), out _channelId))
            {
                throw new Exception("DISCORD_CHANNEL_ID is invalid.");
            }

            _discordSocketClient.Log += LogAsync;

            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ??
                       throw new Exception("DISCORD_BOT_TOKEN not defined.");

            await _discordSocketClient.LoginAsync(TokenType.Bot, token);
            await _discordSocketClient.StartAsync();

            _discordSocketClient.MessageReceived += MessageReceived;

            // Keep the service running until the cancellation token is triggered
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

            // Cleanup
            await _discordSocketClient.StopAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An error occurred in the Discord bot service");
        }
    }

    private async Task MessageReceived(SocketMessage message)
    {
        if (message.Author.IsBot)
        {
            return;
        }

        if (message.Channel.Id == _channelId && (message.MentionedUsers.Count <= 0 ||
                                                message.MentionedUsers.Any(it =>
                                                    it.Id == _discordSocketClient.CurrentUser.Id)))
        {
            StringBuilder addendum = new("### FILE ###");
            bool postAddendum = false;
            foreach (Attachment attachment in message.Attachments.Where(it => it.ContentType.StartsWith("text/")))
            {
                var fileContent = await Downloader.DownloadTestFile(attachment.Url);

                if (fileContent is null)
                {
                    continue;
                }

                postAddendum = true;

                addendum.AppendLine("#FILENAME=" + attachment.Filename);
                addendum.AppendLine(fileContent);
                addendum.AppendLine();
            }

            if (addendum.Length >= FileLengthLimit)
            {
                addendum.Remove(FileLengthLimit, addendum.Length - FileLengthLimit);
                addendum.AppendLine();
                addendum.AppendLine("== THE FILE WAS CUT OFF DUE TO CHARACTER LIMIT ==");
            }
            
            addendum.AppendLine("### FILE END ###");

            // Handle the message without blocking the Discord Client.
            Task.Run(async () =>
            {
                var aiResponse = await _aiClient.CompleteChatAsync(message, postAddendum ? addendum.ToString() : "");
                var responseMessages = aiResponse.SplitByLength(2000, "\n");

                foreach (var responseMessage in responseMessages)
                {
                    await message.Channel.SendMessageAsync(RemoveDiscordPings(responseMessage));
                }
            }).AsyncNoAwait();
        }
    }

    private Task LogAsync(LogMessage logMessage)
    {
        if (logMessage.Exception is CommandException cmdException)
        {
            _logger.LogError(cmdException, 
                $"[Command/{logMessage.Severity}] {cmdException.Command.Aliases[0]} failed to execute in {cmdException.Context.Channel}.");
        }
        else
        {
            _logger.LogInformation($"[General/{logMessage.Severity}] {logMessage}");
        }

        return Task.CompletedTask;
    }
    
    public static string RemoveDiscordPings(string input)
    {
        // Pattern to match different types of Discord mentions
        string pattern = @"<@!?\d+>|<@&\d+>";
        
        // Replace all matches with an empty string
        return Regex.Replace(input, pattern, "");
    }
}