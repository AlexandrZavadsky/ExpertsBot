using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExpertBot
{
    public class BotProccessor
    {
        public readonly TelegramBotClient Bot; 

        private ILogger<BotProccessor> logger;
        private BotOptions settings;

        private bool isPollEnabled;
        private const int VotesNeeded = 3;
        private List<Vote> voters = new List<Vote>();
        private KeyValuePair<string, string> newTitle;
        private const int PollTime = 6 * 60 * 60 * 1000;
        private CancellationTokenSource cancellationToken;

        private Dictionary<string, List<string>> Experts;

        public BotProccessor(ILogger<BotProccessor> logger, IOptions<BotOptions> settings)
        {
            this.logger = logger;
            this.settings = settings.Value;

            Bot = new TelegramBotClient(this.settings.ApiKey);

            LoadExperts();

            Bot.OnCallbackQuery += BotOnCallbackQueryReceived;
            Bot.OnMessage += BotOnMessageReceived;
            Bot.OnMessageEdited += BotOnMessageReceived;
        }

        private async void BotOnCallbackQueryReceived(object sender, CallbackQueryEventArgs callbackQueryEventArgs)
        {
            var data = callbackQueryEventArgs.CallbackQuery.Data;
            var expert = data.Split(' ')[0];
            await Bot.SendTextMessageAsync(data.Split(' ')[1], $"{expert}: \n{string.Join("\n", Experts[expert].Select(x => $"Эксперт по {x}"))} ");
        }

        private void BotOnMessageReceived(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;

            if (message == null || message.Type != MessageType.TextMessage) return;

            var command = message.Text.Split(' ')[0];

            switch (command)
            {
                case "/expert_list":
                    ShowExpertList(message);
                    return;
                case "/add_expert":
                    AddExpert(message);
                    return;
                case "/vote":
                    Vote(message);
                    return;
                case "/poll":                    
                    Poll(message);
                    return;
                case "/help":
                    Help(message);
                    return;

            }
        }

        private void Vote(Message message)
        {
            if (!isPollEnabled)
            {
                SendMessage(message.Chat.Id, "there are no active polls");
                return;
            }
            if (voters.Any(vote => string.Equals(vote.User, message.From.FirstName, StringComparison.OrdinalIgnoreCase)))
            {
                SendMessage(message.Chat.Id, $"{message.From.FirstName} already voted");
                return;
            }
            var parameters = message.Text.Split(' ');
            if (parameters.Length != 2)
            {
                SendMessage(message.Chat.Id, "invalid parameters count");
                return;
            }
            if (string.Equals(parameters[1], "yes", StringComparison.OrdinalIgnoreCase))
            {
                voters.Add(new Vote { User = message.From.FirstName, Value = true });
                if (voters.Count(vote => vote.Value) == VotesNeeded)
                {
                    cancellationToken.Cancel();
                    SendMessage(message.Chat.Id,
                        $"new title 'Эксперт по {newTitle.Value}' was provided for expert {newTitle.Key}");
                    Experts[newTitle.Key].Add(newTitle.Value);
                    SaveExperts();
                    ClearPoll();
                    return;
                }
                SendMessage(message.Chat.Id,
                    $"{message.From.FirstName} voted to approve title 'Эксперт по {newTitle.Value}' for {newTitle.Key}\n {VotesNeeded - voters.Count(vote => vote.Value)} more votes needed to approve title");
            }
            if (string.Equals(parameters[1], "no", StringComparison.OrdinalIgnoreCase))
            {
                voters.Add(new Vote { User = message.From.FirstName, Value = false });
                if (voters.Count(vote => !vote.Value) == VotesNeeded-1)
                {
                    cancellationToken.Cancel();
                    SendMessage(message.Chat.Id,
                        $"new title 'Эксперт по {newTitle.Value}' was rejected for expert {newTitle.Key}");
                    ClearPoll();
                    return;
                }
                SendMessage(message.Chat.Id,
                $"{message.From.FirstName} voted to reject title 'Эксперт по {newTitle.Value}' for {newTitle.Key}\n {VotesNeeded - voters.Count(vote => vote.Value)} more votes needed to approve title");
            }
        }

        private void Poll(Message message)
        {
            if (!isPollEnabled)
            {
                SendMessage(message.Chat.Id, "there are no active polls");
                return;
            }
            SendMessage(message.Chat.Id, $"current poll is for new title 'Эксперт по {newTitle.Value}' for expert {newTitle.Key}");
        }

        private void Help(Message message)
        {
            var usage = "Command list:\n" +
                        "/expert_list - show expert keyboard to choose from\n" +
                        "/help - show all commands\n" +
                        "/add_expert - add new title for expert(usage: add_expert [expert name] [title])\n" +
                        $"expert names: {string.Join(" ", Experts.Keys)}\n" +
                        "/vote - vote for title(usage: /vote [yes/no])" +
                        "/poll - see current poll information";

            Bot.SendTextMessageAsync(message.Chat.Id, usage, replyMarkup: new ReplyKeyboardHide());
        }

        private void AddExpert(Message message)
        {
            if (isPollEnabled)
            {
                SendMessage(message.Chat.Id, "finish started voting first");
                return;
            }
            var parameters = message.Text.Split(' ');
            if (parameters.Length < 3)
            {
                SendMessage(message.Chat.Id, "invalid parameters count");
                return;
            }
            var expert = parameters[1];
            if (!Experts.ContainsKey(expert))
            {
                SendMessage(message.Chat.Id, "expert not found");
                return;
            }
            var title = string.Join(" ", parameters.Skip(2));
            if (Experts.Values.SelectMany(x => x).Any(exTitle => string.Equals(exTitle, title, StringComparison.OrdinalIgnoreCase)))
            {
                SendMessage(message.Chat.Id, $"we already have title 'Эксперт по {title}'");
                return;
            }
            isPollEnabled = true;
            newTitle = new KeyValuePair<string, string>(expert, title);

            cancellationToken = new CancellationTokenSource();
            Task.Delay(PollTime).ContinueWith(_ => StopPoll(message.Chat.Id), cancellationToken.Token);

            SendMessage(message.Chat.Id, "Voting for title started(you have 6 hours to approve title), use '/vote [yes/no]' command to vote");
        }

        private void StopPoll(long chatId)
        {
            ClearPoll();
            SendMessage(chatId, "Time up, voting ended");
        }

        private void ClearPoll()
        {
            isPollEnabled = false;
            newTitle = new KeyValuePair<string, string>();
            voters = new List<Vote>();
        }

        private void ShowExpertList(Message message)
        {
            Bot.SendChatActionAsync(message.Chat.Id, ChatAction.Typing);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    new InlineKeyboardButton("Ваня")
                    {
                        CallbackData = $"Ваня {message.Chat.Id}"
                    },
                    new InlineKeyboardButton("Веталь")
                    {
                        CallbackData = $"Веталь {message.Chat.Id}"
                    }
                },
                new[]
                {
                    new InlineKeyboardButton("Саня")
                    {
                        CallbackData = $"Саня {message.Chat.Id}"
                    },
                    new InlineKeyboardButton("Серёга")
                    {
                        CallbackData = $"Серёга {message.Chat.Id}"
                    }
                }
            });

            Bot.SendTextMessageAsync(message.Chat.Id, "Choose expert", replyMarkup: keyboard);
        }

        private void LoadExperts()
        {
            string json = System.IO.File.ReadAllText(settings.ExpertsFileName);
            Experts = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);
        }

        private void SaveExperts()
        {
            var json = JsonConvert.SerializeObject(Experts);
            System.IO.File.WriteAllText(settings.ExpertsFileName, json);
        }

        private void SendMessage(long chatId, string message)
        {
            Bot.SendTextMessageAsync(chatId, message);
        }
    }
}
