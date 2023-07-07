using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

var botClient = new TelegramBotClient("YOUR_API_KEY");

using CancellationTokenSource cts = new();

Dictionary<long, int> userTopics = new Dictionary<long, int>(); // присваиваем пользователю определённый топик

// StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
ReceiverOptions receiverOptions = new()
{
    AllowedUpdates = Array.Empty<UpdateType>() // receive all update types except ChatMember related updates
};

botClient.StartReceiving(
    updateHandler: HandleUpdateAsync,
    pollingErrorHandler: HandlePollingErrorAsync,
    receiverOptions: receiverOptions,
    cancellationToken: cts.Token
);

var me = await botClient.GetMeAsync();

Console.WriteLine($"Start listening for @{me.Username}");
Console.ReadLine();

// Send cancellation request to stop bot
cts.Cancel();

async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
{
    // Only process Message updates: https://core.telegram.org/bots/api#message
    if (update.Message is not { } message)
        return;

    // Запишим возможные сообщения и стикеры
    var messageText = message.Text;
    var messageSticker = message.Sticker;

    // Проверим, что тип сообщения подходит под наши критерии
    if (messageText is { } && messageSticker is { })
        return;

    var senderChatId = message.Chat.Id; // Записываем айди чата из которого пришло сообщение

    long chatId = "Айди беседы"; // Наша беседа

    // Update сработал:
    Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");

    // Если это ответ оператора, проверяем по тому, что отправка идёт из топика
    if (message.MessageThreadId is not null)
    {
        Console.WriteLine("User chat");

        // Найдём куда нужно отправить и проверим существование в словаре
        var userChatId = userTopics.FirstOrDefault(x => x.Value == message.MessageThreadId).Key;
        if (userChatId == 0)
        {
            Console.WriteLine("This record is not in the dictionary");
            return;
        }

        // Вызываем функцию отправки
        ToClientSender(botClient, userChatId, messageText, messageSticker, cancellationToken);

        return;
    }

    Console.WriteLine("Operator chat");

    // Проверяем, что этот пользователь уже писал нам
    if (userTopics.ContainsKey(senderChatId))
    {   // Берём из словаря айди треда и отправляем по нему сообщение
        int threadId = userTopics[senderChatId];

        ToOperatorSender(botClient, chatId, threadId, messageText, messageSticker, cancellationToken);
    }
    else
    {
        // Создание новой темы для пользователя
        string topicName = $"{message.From.LastName}_{message.From.FirstName}"; // По его ФИ

        var currentTopic = botClient.CreateForumTopicAsync(
            chatId: chatId,
            name: topicName,
            cancellationToken: cancellationToken
            );

        Console.WriteLine("Новый топик создан");

        // Добавление идентификатора темы в словарь
        userTopics[senderChatId] = currentTopic.Result.MessageThreadId;

        // Отправка первого сообщения пользователю
        await botClient.SendTextMessageAsync(
            chatId: senderChatId,
            text: $"Добро пожаловать, {message.From.FirstName}!\nОжидайте ответа оператора",
            cancellationToken: cancellationToken);

        // Отправка сообщения пользователя в тему
        ToOperatorSender(botClient, chatId, userTopics[senderChatId], messageText, messageSticker, cancellationToken);
    }
}

Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
{
    var ErrorMessage = exception switch
    {
        ApiRequestException apiRequestException
            => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
        _ => exception.ToString()
    };

    Console.WriteLine(ErrorMessage);
    return Task.CompletedTask;
}

// Отправщик оператору
async void ToOperatorSender(ITelegramBotClient botClient, long chatId, int topicId, string text, Sticker sticker, CancellationToken cancellationToken)
{
    if (text is not null)
        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "user write:\n" + text,
            messageThreadId: topicId,
            cancellationToken: cancellationToken);
    if (sticker is not null)
        await botClient.SendStickerAsync(
                chatId: chatId,
                sticker: InputFile.FromFileId(sticker!.FileId),
                messageThreadId: topicId,
                cancellationToken: cancellationToken);
}

// Отправщик пользователю
async void ToClientSender(ITelegramBotClient botClient, long chatId, string text, Sticker sticker, CancellationToken cancellationToken)
{
    if (text is not null)
        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "operator write:\n" + text,
            cancellationToken: cancellationToken);
    if (sticker is not null)
        await botClient.SendStickerAsync(
                chatId: chatId,
                sticker: InputFile.FromFileId(sticker!.FileId),
                cancellationToken: cancellationToken);
}
