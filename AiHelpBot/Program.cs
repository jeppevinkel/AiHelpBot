using System.Text;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AiHelpBot;

class Program
{
    private static readonly DiscordSocketClient DiscordSocketClient = new(new DiscordSocketConfig()
    {
        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
    });

    private static readonly AiClient AiClient = new();
    private static ulong _channelId;
    private const int FileLengthLimit = 30000;
    private static QdrantClient _qdrantClient;

    static async Task Main(string[] args)
    {
        _qdrantClient = new QdrantClient("qdrant", 6334);

        if (!(await _qdrantClient.ListCollectionsAsync()).Contains("jellyfin_documentation"))
        {
            await _qdrantClient.CreateCollectionAsync(collectionName: "jellyfin_documentation", vectorsConfig: new VectorParamsMap()
            {
                Map =
                {
                    ["title"] = new VectorParams(){ Distance = Distance.Cosine, Size = 1536},
                    ["content"] = new VectorParams(){ Distance = Distance.Cosine, Size = 1536}
                }
            });
            
            await _qdrantClient.UpsertAsync("jellyfin_documentation", new []
            {
                new PointStruct()
                {
                    Id = 0,
                    Vectors = new Dictionary<string, float[]>()
                    {
                        ["title"] = (await AiClient.GetEmbeddingAsync("Quick Start")).ToArray(),
                        ["content"] = (await AiClient.GetEmbeddingAsync("\n\n    Install Jellyfin on your system with the installation method for your platform.\n\n    Edit the web configuration and adjust the options to fit your desired privacy level.\n        Our defaults sacrifice some absolute self-hosting for often requested features.\n        If this is concerning, please review the documentation and edit accordingly.\n\n    Browse to http://SERVER_IP:8096 to access the included web client.\n\n    Follow the initial setup wizard.\n        Libraries and users can always be added later from the dashboard.\n        Remember the username and password so you can login after the setup.\n\n    Secure the server with a method of your choice.\n        Create an SSL certificate and add it on the Networking page.\n        Put your server behind a reverse proxy.\n        Only allow local connections and refrain from forwarding any ports.\n\n    Enjoy your media!\n")).ToArray(),
                    },
                    Payload =
                    {
                        ["title"] = "Quick Start",
                        ["content"] = "\n\n    Install Jellyfin on your system with the installation method for your platform.\n\n    Edit the web configuration and adjust the options to fit your desired privacy level.\n        Our defaults sacrifice some absolute self-hosting for often requested features.\n        If this is concerning, please review the documentation and edit accordingly.\n\n    Browse to http://SERVER_IP:8096 to access the included web client.\n\n    Follow the initial setup wizard.\n        Libraries and users can always be added later from the dashboard.\n        Remember the username and password so you can login after the setup.\n\n    Secure the server with a method of your choice.\n        Create an SSL certificate and add it on the Networking page.\n        Put your server behind a reverse proxy.\n        Only allow local connections and refrain from forwarding any ports.\n\n    Enjoy your media!\n"
                    }
                },
                new PointStruct()
                {
                    Id = 1,
                    Vectors = new Dictionary<string, float[]>()
                    {
                        ["title"] = (await AiClient.GetEmbeddingAsync("Installation - Container")).ToArray(),
                        ["content"] = (await AiClient.GetEmbeddingAsync("Container images\n\nOfficial container image: jellyfin/jellyfin Docker Pull Count.\n\nLinuxServer.io image: linuxserver/jellyfin Docker Pull Count.\n\nhotio image: hotio/jellyfin Docker Pull Count.\n\nJellyfin distributes official container images on Docker Hub for multiple architectures. These images are based on Debian and built directly from the Jellyfin source code.\n\nAdditionally, there are several third parties providing unofficial container images, including the LinuxServer.io (Dockerfile) project and hotio (Dockerfile), which offer images based on Ubuntu and the official Jellyfin Ubuntu binary packages.\nDocker\n\nDocker allows you to run containers on Linux, Windows and MacOS.\n\nThe basic steps to create and run a Jellyfin container using Docker are as follows.\n\n    Follow the official installation guide to install Docker.\n\n    Download the latest container image.\n\n    docker pull jellyfin/jellyfin\n\nCreate persistent storage for configuration and cache data.\n\nEither create two directories on the host and use bind mounts:\n\nmkdir /path/to/config\nmkdir /path/to/cache\n\nOr create two persistent volumes:\n\ndocker volume create jellyfin-config\ndocker volume create jellyfin-cache\n\n    Create and run a container in one of the following ways.\n\nnote\n\nThe default network mode for Docker is bridge mode. Bridge mode will be used if host mode is omitted. Using host networking (--net=host) is optional but required in order to use DLNA.\n\nUsing Docker command line interface:\n\ndocker run -d \\\n --name jellyfin \\\n --user uid:gid \\\n --net=host \\\n --volume /path/to/config:/config \\ # Alternatively --volume jellyfin-config:/config\n --volume /path/to/cache:/cache \\ # Alternatively --volume jellyfin-cache:/cache\n --mount type=bind,source=/path/to/media,target=/media \\\n --restart=unless-stopped \\\n jellyfin/jellyfin\n\nBind Mounts are needed to pass folders from the host OS to the container OS whereas volumes are maintained by Docker and can be considered easier to backup and control by external programs. For a simple setup, it's considered easier to use Bind Mounts instead of volumes. Multiple media libraries can be bind mounted if needed:\n\n--mount type=bind,source=/path/to/media1,target=/media1\n--mount type=bind,source=/path/to/media2,target=/media2,readonly\n...etc\n\nUsing Docker Compose\n\nCreate a docker-compose.yml file with the following contents. Add in the UID and GID that you would like to run jellyfin as in the user line below, or remove the user line to use the default (root).\n\nservices:\n  jellyfin:\n    image: jellyfin/jellyfin\n    container_name: jellyfin\n    user: uid:gid\n    network_mode: 'host'\n    volumes:\n      - /path/to/config:/config\n      - /path/to/cache:/cache\n      - type: bind\n        source: /path/to/media\n        target: /media\n      - type: bind\n        source: /path/to/media2\n        target: /media2\n        read_only: true\n    restart: 'unless-stopped'\n    # Optional - alternative address used for autodiscovery\n    environment:\n      - JELLYFIN_PublishedServerUrl=http://example.com\n    # Optional - may be necessary for docker healthcheck to pass if running in host network mode\n    extra_hosts:\n      - 'host.docker.internal:host-gateway'\n\nThen while in the same folder as the docker-compose.yml run:\n\ndocker compose up\n\nTo run the container in background add -d to the above command.\n\nYou can learn more about using Docker by reading the official Docker documentation.\nPodman\n\nPodman allows you to run rootless containers. It's also the officially supported container solution on Fedora Linux and its derivatives such as CentOS Stream and RHEL. Steps to run Jellyfin using Podman are similar to the Docker steps.\n\n    Install Podman:\n\n    sudo dnf install -y podman\n\nCreate and run a Jellyfin container:\n\npodman run \\\n --detach \\\n --label \"io.containers.autoupdate=registry\" \\\n --name myjellyfin \\\n --publish 8096:8096/tcp \\\n --rm \\\n --user $(id -u):$(id -g) \\\n --userns keep-id \\\n --volume jellyfin-cache:/cache:Z \\\n --volume jellyfin-config:/config:Z \\\n --mount type=bind,source=/path/to/media,destination=/media,ro=true,relabel=private \\\n docker.io/jellyfin/jellyfin:latest\n\nOpen the necessary ports in your machine's firewall if you wish to permit access to the Jellyfin server from outside the host. This is not done automatically when using rootless Podman. If your distribution uses firewalld, the following commands save and load a new firewall rule opening the HTTP port 8096 for TCP connections.\n\nsudo firewall-cmd --add-port=8096/tcp --permanent\nsudo firewall-cmd --reload\n\nPodman doesn't require root access to run containers, although there are some details to be mindful of; see the relevant documentation. For security, the Jellyfin container should be run using rootless Podman. Furthermore, it is safer to run as a non-root user within the container. The --user option will run with the provided user id and group id inside the container. The --userns keep-id flag ensures that current user's id is mapped to the non-root user's id inside the container. This ensures that the permissions for directories bind-mounted inside the container are mapped correctly between the user running Podman and the user running Jellyfin inside the container.\n\nKeep in mind that the --label \"io.containers.autoupdate=image\" flag will allow the container to be automatically updated via podman auto-update.\n\nThe z (shared volume) or Z (private volume) volume option and relabel=shared or relabel=private mount option tell Podman to relabel files inside the volumes as appropriate, for systems running SELinux.\n\nReplace jellyfin-config and jellyfin-cache with /path/to/config and /path/to/cache if you wish to use bind mounts.\n\nThis example mounts your media library read-only by setting ro=true; set this to ro=false if you wish to give Jellyfin write access to your media.\nManaging via Systemd\n\nTo run as a systemd service see podman-systemd.unit.\n\nAs always it is recommended to run the container rootless. Therefore we want to manage the container with the systemd --user flag.\n\n    Create a new user that the rootless container will run under.\n\n    useradd jellyfin\n\n    This allows users who are not logged in to run long-running services.\n\n    loginctl enable-linger jellyfin\n\n    Open an interactive shell session.\n\n    machinectl shell jellyfin@\n\n    Install .config/containers/systemd/jellyfin.container\n\n        Contents of ~/.config/containers/systemd/jellyfin.container\n\n    [Container]\n    Image=docker.io/jellyfin/jellyfin:latest\n    AutoUpdate=registry\n    PublishPort=8096:8096/tcp\n    UserNS=keep-id\n    Volume=jellyfin-config:/config:Z\n    Volume=jellyfin-cache:/cache:Z\n    Volume=jellyfin-media:/media:Z\n\n    [Service]\n    # Inform systemd of additional exit status\n    SuccessExitStatus=0 143\n\n    [Install]\n    # Start by default on boot\n    WantedBy=default.target\n\n    Reload daemon and start the service.\n\n    systemctl --user daemon-reload\n\n    systemctl --user start jellyfin\n\n    To enable Podman auto-updates, enable the necessary systemd timer.\n\n    systemctl --user enable --now podman-auto-update.timer\n\n    Optionally check logs for errors\n\n    journalctl --user -u jellyfin\n\n    exit the current session.\n\nWith hardware acceleration\n\nTo use hardware acceleration, you need to allow the container to access the render device. If you are using container-selinux-2.226 or later, you have to set the container_use_dri_devices flag in selinux or the container will not be able to use it:\n\nsudo setsebool -P container_use_dri_devices 1\n\nOn older versions of container-selinux, you have to disable the selinux confinement for the container by adding --security-opt label=disable to the podman command.\n\nThen, you need to mount the render device inside the container:\n\n--device /dev/dri/:/dev/dri/\n\nFinally, you need to set the --device flag for the container to use the render device:\n\n--device /dev/dri/\npodman run\n\n   podman run \\\n    --detach \\\n    --label \"io.containers.autoupdate=registry\" \\\n    --name myjellyfin \\\n    --publish 8096:8096/tcp \\\n    --device /dev/dri/:/dev/dri/ \\\n    # --security-opt label=disable # Only needed for older versions of container-selinux < 2.226\n    --rm \\\n    --user $(id -u):$(id -g) \\\n    --userns keep-id \\\n    --volume jellyfin-cache:/cache:Z \\\n    --volume jellyfin-config:/config:Z \\\n    --mount type=bind,source=/path/to/media,destination=/media,ro=true,relabel=private \\\n    docker.io/jellyfin/jellyfin:latest\n\nsystemd\n\n[Unit]\nDescription=jellyfin\n\n[Container]\nImage=docker.io/jellyfin/jellyfin:latest\nAutoUpdate=registry\nPublishPort=8096:8096/tcp\nUserNS=keep-id\n#SecurityLabelDisable=true # Only needed for older versions of container-selinux < 2.226\nAddDevice=/dev/dri/:/dev/dri/\nVolume=jellyfin-config:/config:Z\nVolume=jellyfin-cache:/cache:Z\nVolume=jellyfin-media:/media:Z\n\n[Service]\n# Inform systemd of additional exit status\nSuccessExitStatus=0 143\n\n[Install]\n# Start by default on boot\nWantedBy=default.target\n\nTrueNAS SCALE / TrueCharts\n\nJellyfin is available as a TrueNAS SCALE App inside the TrueCharts App Catalog with direct integration into the GUI, no CLI needed. Direct support is available on the TrueCharts Discord and the source code is available on GitHub.\n\n    Install the TrueCharts Catalog to TrueNAS SCALE, see website for more info.\n        Go to Apps page from the top level SCALE menu\n        Select Manage Catalogs tab on the Apps page\n        Click Add Catalog\n        After reading the iXsystems notice, click Continue and enter the required information: Name: truecharts Repository: https://github.com/truecharts/catalog Preferred Trains: enterprise and stable Branch: main\n        Click Save and allow SCALE to refresh its catalog with TrueCharts (this may take a few minutes)\n\n    Click Available Applications and search for Jellyfin\n\n    Click Install, which will take you to the GUI Wizard and you'll be able to fill out the necessary info\n        Server URL to publish in UDP Auto Discovery response.\n        Networking, Ingress (Reverse Proxy), Security Options\n        Adding Storage (for media folders) is also a standalone guide available in the TrueCharts documentation. For Jellyfin the recommendation is to add storage as Additional App Storage\n\n    Click Save and once it's up and running you'll be able to click Open to access Jellyfin.\n")).ToArray(),
                    },
                    Payload =
                    {
                        ["title"] = "Installation - Container",
                        ["content"] = "Container images\n\nOfficial container image: jellyfin/jellyfin Docker Pull Count.\n\nLinuxServer.io image: linuxserver/jellyfin Docker Pull Count.\n\nhotio image: hotio/jellyfin Docker Pull Count.\n\nJellyfin distributes official container images on Docker Hub for multiple architectures. These images are based on Debian and built directly from the Jellyfin source code.\n\nAdditionally, there are several third parties providing unofficial container images, including the LinuxServer.io (Dockerfile) project and hotio (Dockerfile), which offer images based on Ubuntu and the official Jellyfin Ubuntu binary packages.\nDocker\n\nDocker allows you to run containers on Linux, Windows and MacOS.\n\nThe basic steps to create and run a Jellyfin container using Docker are as follows.\n\n    Follow the official installation guide to install Docker.\n\n    Download the latest container image.\n\n    docker pull jellyfin/jellyfin\n\nCreate persistent storage for configuration and cache data.\n\nEither create two directories on the host and use bind mounts:\n\nmkdir /path/to/config\nmkdir /path/to/cache\n\nOr create two persistent volumes:\n\ndocker volume create jellyfin-config\ndocker volume create jellyfin-cache\n\n    Create and run a container in one of the following ways.\n\nnote\n\nThe default network mode for Docker is bridge mode. Bridge mode will be used if host mode is omitted. Using host networking (--net=host) is optional but required in order to use DLNA.\n\nUsing Docker command line interface:\n\ndocker run -d \\\n --name jellyfin \\\n --user uid:gid \\\n --net=host \\\n --volume /path/to/config:/config \\ # Alternatively --volume jellyfin-config:/config\n --volume /path/to/cache:/cache \\ # Alternatively --volume jellyfin-cache:/cache\n --mount type=bind,source=/path/to/media,target=/media \\\n --restart=unless-stopped \\\n jellyfin/jellyfin\n\nBind Mounts are needed to pass folders from the host OS to the container OS whereas volumes are maintained by Docker and can be considered easier to backup and control by external programs. For a simple setup, it's considered easier to use Bind Mounts instead of volumes. Multiple media libraries can be bind mounted if needed:\n\n--mount type=bind,source=/path/to/media1,target=/media1\n--mount type=bind,source=/path/to/media2,target=/media2,readonly\n...etc\n\nUsing Docker Compose\n\nCreate a docker-compose.yml file with the following contents. Add in the UID and GID that you would like to run jellyfin as in the user line below, or remove the user line to use the default (root).\n\nservices:\n  jellyfin:\n    image: jellyfin/jellyfin\n    container_name: jellyfin\n    user: uid:gid\n    network_mode: 'host'\n    volumes:\n      - /path/to/config:/config\n      - /path/to/cache:/cache\n      - type: bind\n        source: /path/to/media\n        target: /media\n      - type: bind\n        source: /path/to/media2\n        target: /media2\n        read_only: true\n    restart: 'unless-stopped'\n    # Optional - alternative address used for autodiscovery\n    environment:\n      - JELLYFIN_PublishedServerUrl=http://example.com\n    # Optional - may be necessary for docker healthcheck to pass if running in host network mode\n    extra_hosts:\n      - 'host.docker.internal:host-gateway'\n\nThen while in the same folder as the docker-compose.yml run:\n\ndocker compose up\n\nTo run the container in background add -d to the above command.\n\nYou can learn more about using Docker by reading the official Docker documentation.\nPodman\n\nPodman allows you to run rootless containers. It's also the officially supported container solution on Fedora Linux and its derivatives such as CentOS Stream and RHEL. Steps to run Jellyfin using Podman are similar to the Docker steps.\n\n    Install Podman:\n\n    sudo dnf install -y podman\n\nCreate and run a Jellyfin container:\n\npodman run \\\n --detach \\\n --label \"io.containers.autoupdate=registry\" \\\n --name myjellyfin \\\n --publish 8096:8096/tcp \\\n --rm \\\n --user $(id -u):$(id -g) \\\n --userns keep-id \\\n --volume jellyfin-cache:/cache:Z \\\n --volume jellyfin-config:/config:Z \\\n --mount type=bind,source=/path/to/media,destination=/media,ro=true,relabel=private \\\n docker.io/jellyfin/jellyfin:latest\n\nOpen the necessary ports in your machine's firewall if you wish to permit access to the Jellyfin server from outside the host. This is not done automatically when using rootless Podman. If your distribution uses firewalld, the following commands save and load a new firewall rule opening the HTTP port 8096 for TCP connections.\n\nsudo firewall-cmd --add-port=8096/tcp --permanent\nsudo firewall-cmd --reload\n\nPodman doesn't require root access to run containers, although there are some details to be mindful of; see the relevant documentation. For security, the Jellyfin container should be run using rootless Podman. Furthermore, it is safer to run as a non-root user within the container. The --user option will run with the provided user id and group id inside the container. The --userns keep-id flag ensures that current user's id is mapped to the non-root user's id inside the container. This ensures that the permissions for directories bind-mounted inside the container are mapped correctly between the user running Podman and the user running Jellyfin inside the container.\n\nKeep in mind that the --label \"io.containers.autoupdate=image\" flag will allow the container to be automatically updated via podman auto-update.\n\nThe z (shared volume) or Z (private volume) volume option and relabel=shared or relabel=private mount option tell Podman to relabel files inside the volumes as appropriate, for systems running SELinux.\n\nReplace jellyfin-config and jellyfin-cache with /path/to/config and /path/to/cache if you wish to use bind mounts.\n\nThis example mounts your media library read-only by setting ro=true; set this to ro=false if you wish to give Jellyfin write access to your media.\nManaging via Systemd\n\nTo run as a systemd service see podman-systemd.unit.\n\nAs always it is recommended to run the container rootless. Therefore we want to manage the container with the systemd --user flag.\n\n    Create a new user that the rootless container will run under.\n\n    useradd jellyfin\n\n    This allows users who are not logged in to run long-running services.\n\n    loginctl enable-linger jellyfin\n\n    Open an interactive shell session.\n\n    machinectl shell jellyfin@\n\n    Install .config/containers/systemd/jellyfin.container\n\n        Contents of ~/.config/containers/systemd/jellyfin.container\n\n    [Container]\n    Image=docker.io/jellyfin/jellyfin:latest\n    AutoUpdate=registry\n    PublishPort=8096:8096/tcp\n    UserNS=keep-id\n    Volume=jellyfin-config:/config:Z\n    Volume=jellyfin-cache:/cache:Z\n    Volume=jellyfin-media:/media:Z\n\n    [Service]\n    # Inform systemd of additional exit status\n    SuccessExitStatus=0 143\n\n    [Install]\n    # Start by default on boot\n    WantedBy=default.target\n\n    Reload daemon and start the service.\n\n    systemctl --user daemon-reload\n\n    systemctl --user start jellyfin\n\n    To enable Podman auto-updates, enable the necessary systemd timer.\n\n    systemctl --user enable --now podman-auto-update.timer\n\n    Optionally check logs for errors\n\n    journalctl --user -u jellyfin\n\n    exit the current session.\n\nWith hardware acceleration\n\nTo use hardware acceleration, you need to allow the container to access the render device. If you are using container-selinux-2.226 or later, you have to set the container_use_dri_devices flag in selinux or the container will not be able to use it:\n\nsudo setsebool -P container_use_dri_devices 1\n\nOn older versions of container-selinux, you have to disable the selinux confinement for the container by adding --security-opt label=disable to the podman command.\n\nThen, you need to mount the render device inside the container:\n\n--device /dev/dri/:/dev/dri/\n\nFinally, you need to set the --device flag for the container to use the render device:\n\n--device /dev/dri/\npodman run\n\n   podman run \\\n    --detach \\\n    --label \"io.containers.autoupdate=registry\" \\\n    --name myjellyfin \\\n    --publish 8096:8096/tcp \\\n    --device /dev/dri/:/dev/dri/ \\\n    # --security-opt label=disable # Only needed for older versions of container-selinux < 2.226\n    --rm \\\n    --user $(id -u):$(id -g) \\\n    --userns keep-id \\\n    --volume jellyfin-cache:/cache:Z \\\n    --volume jellyfin-config:/config:Z \\\n    --mount type=bind,source=/path/to/media,destination=/media,ro=true,relabel=private \\\n    docker.io/jellyfin/jellyfin:latest\n\nsystemd\n\n[Unit]\nDescription=jellyfin\n\n[Container]\nImage=docker.io/jellyfin/jellyfin:latest\nAutoUpdate=registry\nPublishPort=8096:8096/tcp\nUserNS=keep-id\n#SecurityLabelDisable=true # Only needed for older versions of container-selinux < 2.226\nAddDevice=/dev/dri/:/dev/dri/\nVolume=jellyfin-config:/config:Z\nVolume=jellyfin-cache:/cache:Z\nVolume=jellyfin-media:/media:Z\n\n[Service]\n# Inform systemd of additional exit status\nSuccessExitStatus=0 143\n\n[Install]\n# Start by default on boot\nWantedBy=default.target\n\nTrueNAS SCALE / TrueCharts\n\nJellyfin is available as a TrueNAS SCALE App inside the TrueCharts App Catalog with direct integration into the GUI, no CLI needed. Direct support is available on the TrueCharts Discord and the source code is available on GitHub.\n\n    Install the TrueCharts Catalog to TrueNAS SCALE, see website for more info.\n        Go to Apps page from the top level SCALE menu\n        Select Manage Catalogs tab on the Apps page\n        Click Add Catalog\n        After reading the iXsystems notice, click Continue and enter the required information: Name: truecharts Repository: https://github.com/truecharts/catalog Preferred Trains: enterprise and stable Branch: main\n        Click Save and allow SCALE to refresh its catalog with TrueCharts (this may take a few minutes)\n\n    Click Available Applications and search for Jellyfin\n\n    Click Install, which will take you to the GUI Wizard and you'll be able to fill out the necessary info\n        Server URL to publish in UDP Auto Discovery response.\n        Networking, Ingress (Reverse Proxy), Security Options\n        Adding Storage (for media folders) is also a standalone guide available in the TrueCharts documentation. For Jellyfin the recommendation is to add storage as Additional App Storage\n\n    Click Save and once it's up and running you'll be able to click Open to access Jellyfin.\n"
                    }
                }
            });
        }

        var searchResult =
            await SearchEmbeddings(_qdrantClient, AiClient, "How can I install jellyfin in a docker container?");

        foreach (ScoredPoint scoredPoint in searchResult)
        {
            Console.WriteLine($"{scoredPoint.Payload["title"]} (Score: {scoredPoint.Score})");
        }
        
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

        if (message.Channel.Id == _channelId && (message.MentionedUsers.Count <= 0 ||
                                                 message.MentionedUsers.Any(it =>
                                                     it.Id == DiscordSocketClient.CurrentUser.Id)))
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
                var aiResponse = await AiClient.CompleteChatAsync(message, postAddendum ? addendum.ToString() : "");
                var responseMessages = aiResponse.SplitByLength(2000, "\n");

                foreach (var responseMessage in responseMessages)
                {
                    await message.Channel.SendMessageAsync(responseMessage);
                }
            }).AsyncNoAwait();

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

    private static async Task<IReadOnlyList<ScoredPoint>> SearchEmbeddings(QdrantClient qdrantClient, AiClient aiClient,
        string query)
    {
        var embedding = await aiClient.GetEmbeddingAsync(query);

        return await qdrantClient.SearchAsync("jellyfin_documentation", embedding, vectorName: "content", limit: 3);
    }
}