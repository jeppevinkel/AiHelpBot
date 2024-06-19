# AiHelpBot
This bot is supplied as a Docker image, making it simple to run.

For now, it doesn't support a general system message, so it can't be used for a general purpose **yet**.

Example docker compose file:
```yaml
version: "3.8"
services:
  ai-help-bot:
    image: ghcr.io/jeppevinkel/ai-help-bot:latest
    restart: unless-stopped
    environment:
      - OPENAI_API_KEY=
      - DISCORD_CHANNEL_ID=
      - DISCORD_BOT_TOKEN=
      - MESSAGE_BUFFER_SIZE=6
```
