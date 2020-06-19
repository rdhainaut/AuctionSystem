﻿namespace AuctionSystem.Worker.Runner
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Application.Common.Interfaces;
    using Common;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Persistence;

    public class JobManager
    {
        private const string AppMainEmailAddress = "mytestedauctionsystem@gmail.com";

        private const string CongratsMessage =
            "Congratulations {0}! You won bid for item - {1}. You will be contacted shortly by the seller for additional information.";

        private const string CongratsMessageForItemSeller =
            "Congratulations {0}! Your item - {1} - was sucessfully sold for €{2}. Please contact the buyer to arrange the shipping and etc. Buyer email - {3}.";

        private const string LogMessage =
            "Email was sent successfully on {0} utc time to {1} regarding his winning of item {2}";

        private const string LogMessageForItemOwner =
            "Email was sent successfully on {0} utc time to {1} regarding of {2} being sold";

        private const string ExceptionMessage = "Entity update failed. Exception message: {0}";
        private readonly IDateTime dateTime;
        private readonly AuctionSystemDbContext dbContext;
        private readonly IEmailSender emailSender;
        private readonly ILogger logger;

        public JobManager(AuctionSystemDbContext dbContext,
            IEmailSender emailSender,
            ILogger<JobManager> logger,
            IDateTime dateTime)
        {
            this.dbContext = dbContext;
            this.emailSender = emailSender;
            this.logger = logger;
            this.dateTime = dateTime;
        }

        public async Task ExecuteAllJobs(int repeatTimeInMinutes = 1)
        {
            // Add your Jobs/Tasks here
            await this.SendEmailToTheWinnersOfGivenBids();

            var millisecondsTimeOut = repeatTimeInMinutes * 60_000;
            Thread.Sleep(millisecondsTimeOut);
        }

        private async Task SendEmailToTheWinnersOfGivenBids()
        {
            var items = await this.dbContext.Items
                .Where(i => i.EndTime <= this.dateTime.UtcNow && !i.IsEmailSent && i.Bids.Count >= 1)
                .Select(i => new ItemDto
                {
                    Id = i.Id,
                    Title = i.Title,
                    IsEmailSent = i.IsEmailSent,
                    WinnerAmount = i.Bids.Max(b => b.Amount),
                    UserEmail = i.User.Email,
                    UserFullName = i.User.FullName
                })
                .ToListAsync();

            foreach (var item in items)
            {
                var winnerBid = await this.dbContext.Bids
                    .Where(b => b.Amount == item.WinnerAmount && b.ItemId == item.Id)
                    .Select(b => new
                    {
                        UserEmail = b.User.Email,
                        UserFullName = b.User.FullName,
                        b.Amount
                    })
                    .SingleOrDefaultAsync();

                var isEmailSendToItemOwner = await this.emailSender.SendEmailAsync(AppMainEmailAddress, item.UserEmail,
                    "Your item was sold!",
                    string.Format(CongratsMessageForItemSeller, item.UserFullName, item.Title, winnerBid.Amount,
                        winnerBid.UserEmail));
                var isSuccessful = await this.emailSender.SendEmailAsync(AppMainEmailAddress, winnerBid.UserEmail,
                    "You won a bid!",
                    string.Format(CongratsMessage, winnerBid.UserFullName, item.Title));

                if (!isSuccessful || isEmailSendToItemOwner)
                {
                    continue;
                }

                try
                {
                    var dbItem = await this.dbContext.Items.FindAsync(item.Id);
                    if (dbItem == null)
                    {
                        continue;
                    }

                    dbItem.IsEmailSent = true;
                    this.dbContext.Items.Update(dbItem);
                    await this.dbContext.SaveChangesAsync();
                    this.logger.LogInformation(string.Format(LogMessage, this.dateTime.UtcNow, winnerBid.UserEmail,
                        item.Title));
                    this.logger.LogInformation(string.Format(LogMessageForItemOwner, this.dateTime.UtcNow,
                        item.UserEmail,
                        item.Title));
                }
                catch (Exception ex)
                {
                    this.logger.LogInformation(string.Format(ExceptionMessage, ex.Message));
                }
            }
        }
    }
}