using Summoning.Bot;
using System;
using System.Collections.Generic;

namespace Summoning
{
    interface IDatabase
    {
        /*
         * Does the current database contain valid accounts that can be used.
         */
        bool ContainValidAccounts(int count = 1);

        /*
         * Fetches the first batch of accounts in a single query.
         */
        List<Account> FetchInitialAccounts(int length = 6);


        /*
         * Fetches a single account.
         */
        Account FetchAccount();

        /*
         * Set the account to finished int he database.
         */
        void SetFinished(Account account);

        void SetProgress(Account account, int progress = 1);
    }
}
