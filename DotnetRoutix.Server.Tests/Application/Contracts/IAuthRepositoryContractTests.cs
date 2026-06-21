using DotnetRoutix.Server.Application.Contracts;
using DotnetRoutix.Server.Domain.Entities;
using Xunit;

namespace DotnetRoutix.Server.Tests.Application.Contracts;

public sealed class IAuthRepositoryContractTests
{
    [Fact]
    public async Task InMemoryRepository_ImplementsExpectedContractBehavior()
    {
        IAuthRepository repository = new InMemoryAuthRepository();

        await repository.EnsureUserAsync(new UserAccount
        {
            FullName = "Aarav Sharma",
            Email = "demo@lunchbox.local",
            Password = "LunchBox@123",
            SecurityPin = "4821",
            MobileNumber = "+91 98765 43210",
            DefaultPickupAddress = "Market Street"
        });

        var user = await repository.FindUserByCredentialsAsync("demo@lunchbox.local", "LunchBox@123");
        Assert.NotNull(user);

        user.LastIssuedPin = "9999";
        await repository.ReplaceUserAsync(user);

        var byId = await repository.FindUserByIdAsync(user.Id);
        Assert.NotNull(byId);
        Assert.Equal("9999", byId.LastIssuedPin);

        var firstUser = await repository.GetFirstUserAsync();
        Assert.NotNull(firstUser);
        Assert.Equal("demo@lunchbox.local", firstUser.Email);
    }

    private sealed class InMemoryAuthRepository : IAuthRepository
    {
        private readonly List<UserAccount> _users = new();

        public Task<UserAccount?> GetFirstUserAsync()
            => Task.FromResult(_users.FirstOrDefault());

        public Task<UserAccount?> FindUserByCredentialsAsync(string email, string password)
            => Task.FromResult(_users.FirstOrDefault(user => user.Email == email && user.Password == password));

        public Task<UserAccount?> FindUserByIdAsync(int id)
            => Task.FromResult(_users.FirstOrDefault(user => user.Id == id));

        public Task<UserAccount?> FindUserByTempOtpTokenAsync(string tempOtpToken)
            => Task.FromResult(_users.FirstOrDefault(user => user.TempOtpToken == tempOtpToken));

        public Task<UserAccount?> FindUserByEmailAsync(string email)
            => Task.FromResult(_users.FirstOrDefault(user => user.Email == email));

        public Task<UserAccount?> FindUserByUsernameAsync(string username)
            => Task.FromResult(_users.FirstOrDefault(user => user.Username == username));

        public Task<UserAccount?> FindUserByUsernameAndPasswordAsync(string username, string password)
            => Task.FromResult(_users.FirstOrDefault(user => user.Username == username && user.Password == password));

        public Task<UserAccount?> CreateUserAsync(UserAccount account)
        {
            account.Id = _users.Count == 0 ? 1 : _users.Max(user => user.Id) + 1;
            _users.Add(account);
            return Task.FromResult<UserAccount?>(account);
        }

        public Task ReplaceUserAsync(UserAccount account)
        {
            var index = _users.FindIndex(user => user.Id == account.Id);
            if (index >= 0)
            {
                _users[index] = account;
            }

            return Task.CompletedTask;
        }

        public async Task EnsureUserAsync(UserAccount account)
        {
            var existingUser = await FindUserByEmailAsync(account.Email);
            if (existingUser is not null)
            {
                account.Id = existingUser.Id;
                account.LastLoginAtUtc = existingUser.LastLoginAtUtc;
                await ReplaceUserAsync(account);
                return;
            }

            account.Id = _users.Count == 0 ? 1 : _users.Max(user => user.Id) + 1;
            _users.Add(account);
        }
    }
}
