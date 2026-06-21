using DotnetRoutix.Server.Domain.Entities;

namespace DotnetRoutix.Server.Application.Contracts;

public interface IAuthRepository
{
    Task<UserAccount?> GetFirstUserAsync();

    Task<UserAccount?> FindUserByCredentialsAsync(string email, string password);

    Task<UserAccount?> FindUserByIdAsync(int id);

    Task<UserAccount?> FindUserByEmailAsync(string email);

    Task<UserAccount?> FindUserByUsernameAsync(string username);

    Task<UserAccount?> FindUserByTempOtpTokenAsync(string tempOtpToken);

    Task ReplaceUserAsync(UserAccount account);

    Task EnsureUserAsync(UserAccount account);

    Task<UserAccount?> CreateUserAsync(UserAccount account);

    Task<UserAccount?> FindUserByUsernameAndPasswordAsync(string username, string password);
}
