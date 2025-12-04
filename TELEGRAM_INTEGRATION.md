# Telegram Integration

This application includes Telegram bot integration for sending notifications and alerts.

## Setup

### 1. Create a Telegram Bot

1. Open Telegram and search for [@BotFather](https://t.me/botfather)
2. Send `/newbot` command
3. Follow the instructions to create your bot
4. Copy the bot token provided by BotFather

### 2. Configure the Bot

You can configure the Telegram bot in two ways:

#### Option A: Using the Settings UI (Recommended)

1. Navigate to the Settings page in the application
2. Scroll to the "Telegram Integration" section
3. Enable the Telegram Bot toggle
4. Enter your bot token
5. (Optional) Enter a webhook URL for production use
6. Click "Save Configuration"

#### Option B: Direct Database Configuration

The bot configuration is stored in the `TelegramConfigs` table in the database. You can also configure it directly:

```sql
INSERT INTO TelegramConfigs (botToken, webhookUrl, isEnabled, created_at, updated_at)
VALUES ('YOUR_BOT_TOKEN', '', 1, datetime('now'), datetime('now'));
```

### 3. Get Your Chat ID

1. Start a chat with your bot on Telegram
2. Send `/start` command
3. Send `/chatid` command
4. The bot will reply with your Chat ID
5. Use this Chat ID to test the connection in the Settings page

### 4. Test the Connection

1. In the Settings page, scroll to "Test Connection"
2. Enter your Chat ID
3. Click "Send Test Message"
4. You should receive a test message from your bot

## Available Commands

When chatting with your bot:

- `/start` - Initialize the bot and get a welcome message
- `/help` - Display available commands
- `/status` - Check if the bot is running
- `/chatid` - Get your Telegram Chat ID

## Webhook vs Polling Mode

### Polling Mode (Default)
- No webhook URL configured
- Bot checks for messages periodically
- Suitable for development and testing
- No public URL required

### Webhook Mode (Production)
- Configure a webhook URL in settings
- Telegram sends updates to your server
- More efficient for production
- Requires a public HTTPS URL

To set up webhook mode:
1. Deploy your application with HTTPS
2. Set the webhook URL to: `https://yourdomain.com/api/telegram/webhook`
3. Save the configuration

## API Endpoints

### Get Configuration
```http
GET /api/telegram/config
```

Returns the current Telegram bot configuration.

### Save Configuration
```http
POST /api/telegram/config
Content-Type: application/json

{
  "botToken": "your-bot-token",
  "webhookUrl": "https://yourdomain.com/api/telegram/webhook",
  "isEnabled": true
}
```

### Test Connection
```http
POST /api/telegram/test
Content-Type: application/json

{
  "chatId": 123456789
}
```

### Send Message
```http
POST /api/telegram/send
Content-Type: application/json

{
  "chatId": 123456789,
  "message": "Your message here"
}
```

Or send by username:
```http
POST /api/telegram/send
Content-Type: application/json

{
  "username": "telegram_username",
  "message": "Your message here"
}
```

### Webhook Management
```http
POST /api/telegram/webhook/set
Content-Type: application/json

{
  "webhookUrl": "https://yourdomain.com/api/telegram/webhook"
}
```

```http
POST /api/telegram/webhook/delete
```

## Architecture

### Components

1. **TelegramService** (`Services/TelegramService.cs`)
   - Core service for Telegram bot operations
   - Reads configuration from database
   - Handles message sending and receiving
   - Manages webhook setup

2. **TelegramController** (`Controllers/TelegramController.cs`)
   - REST API endpoints for Telegram operations
   - Configuration management (CRUD)
   - Webhook handling
   - Test message functionality

3. **TelegramHostedService** (`HostedServices/TelegramHostedService.cs`)
   - Initializes bot on application startup
   - Configures webhook if URL is provided
   - Reads configuration from database

4. **TelegramConfig Model** (`Models/TelegramConfig.cs`)
   - Database model for bot configuration
   - Stores bot token, webhook URL, and enabled status

5. **Settings UI** (`src/client/src/pages/Settings.tsx`)
   - User interface for bot configuration
   - Enable/disable bot
   - Configure token and webhook
   - Test connection

### Database Schema

```sql
CREATE TABLE TelegramConfigs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    botToken TEXT NOT NULL,
    webhookUrl TEXT,
    isEnabled INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
```

## Security Notes

1. **Never commit your bot token** to version control
2. Store the token securely in the database
3. Use HTTPS for webhook URLs in production
4. The bot token is stored as a password field in the UI
5. Validate all incoming webhook requests
6. Consider implementing rate limiting for API endpoints

## Troubleshooting

### Bot not responding
1. Check if the bot is enabled in Settings
2. Verify the bot token is correct
3. Check application logs for errors
4. Ensure the bot is started (send `/start` command)

### Webhook not working
1. Verify the webhook URL is publicly accessible
2. Ensure the URL uses HTTPS
3. Check if the webhook is set correctly: `POST /api/telegram/webhook/set`
4. Review server logs for incoming webhook requests

### Test message not received
1. Verify you've started a chat with the bot
2. Check that the Chat ID is correct
3. Ensure the bot is enabled in Settings
4. Check that the bot token is valid

## Development

To extend the bot functionality:

1. Add new command handlers in `TelegramService.HandleMessageAsync()`
2. Create new API endpoints in `TelegramController`
3. Update the UI in `Settings.tsx` for new features
4. Add new database fields to `TelegramConfig` if needed

Example: Adding a new command
```csharp
else if (messageText.StartsWith("/mycommand"))
{
    await client.SendMessage(
        chatId,
        "Response to my command");
}
```
