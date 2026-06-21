using DotnetRoutix.Server.Application.Contracts;
using DotnetRoutix.Server.Domain.Entities;
using DotnetRoutix.Server.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace DotnetRoutix.Server.Infrastructure.Repositories;

public sealed class MongoAuthRepository : IAuthRepository
{
    private readonly IMongoCollection<UserAccount> _users;

    public MongoAuthRepository(IOptions<MongoDbOptions> options)
    {
        var mongoDbOptions = options.Value;

        var client = new MongoClient(mongoDbOptions.ConnectionString);
        var database = client.GetDatabase(mongoDbOptions.DatabaseName);
        _users = database.GetCollection<UserAccount>("users");

        var emailIndex = new CreateIndexModel<UserAccount>(
            Builders<UserAccount>.IndexKeys.Ascending(account => account.Email),
            new CreateIndexOptions { Unique = true });

        _users.Indexes.CreateOne(emailIndex);
    }

    public async Task<UserAccount?> GetFirstUserAsync()
        => await _users.Find(Builders<UserAccount>.Filter.Empty).FirstOrDefaultAsync();

    public async Task<UserAccount?> FindUserByCredentialsAsync(string email, string password)
        => await _users.Find(user => user.Email == email && user.Password == password).FirstOrDefaultAsync();

    public async Task<UserAccount?> FindUserByIdAsync(int id)
        => await _users.Find(user => user.Id == id).FirstOrDefaultAsync();

    public async Task<UserAccount?> FindUserByEmailAsync(string email)
        => await _users.Find(user => user.Email == email).FirstOrDefaultAsync();

    public Task ReplaceUserAsync(UserAccount account)
        => _users.ReplaceOneAsync(user => user.Id == account.Id, account);

    public async Task EnsureUserAsync(UserAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var existingUser = await FindUserByEmailAsync(account.Email);
        if (existingUser is not null)
        {
            account.Id = existingUser.Id;
            account.LastLoginAtUtc = existingUser.LastLoginAtUtc;
            await ReplaceUserAsync(account);
            return;
        }

        account.Id = await GetNextIdAsync();
        await _users.InsertOneAsync(account);
    }

    public async Task<UserAccount?> FindUserByUsernameAsync(string username)
        => await _users.Find(user => user.Username == username).FirstOrDefaultAsync();

    public async Task<UserAccount?> FindUserByTempOtpTokenAsync(string tempOtpToken)
        => await _users.Find(user => user.TempOtpToken == tempOtpToken).FirstOrDefaultAsync();

    public async Task<UserAccount?> FindUserByUsernameAndPasswordAsync(string username, string password)
        => await _users.Find(user => user.Username == username && user.Password == password).FirstOrDefaultAsync();

    public async Task<UserAccount?> CreateUserAsync(UserAccount account)
    {
        account.Id = await GetNextIdAsync();
        await _users.InsertOneAsync(account);
        return account;
    }

    private async Task<int> GetNextIdAsync()
    {
        var lastUser = await _users
            .Find(Builders<UserAccount>.Filter.Empty)
            .SortByDescending(user => user.Id)
            .Limit(1)
            .FirstOrDefaultAsync();

        return (lastUser?.Id ?? 0) + 1;
    }
}
