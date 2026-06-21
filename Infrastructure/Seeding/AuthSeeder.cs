using DotnetRoutix.Server.Application.Contracts;
using DotnetRoutix.Server.Domain.Entities;

namespace DotnetRoutix.Server.Infrastructure.Seeding;

public sealed class AuthSeeder : IAuthSeeder
{
    private readonly IAuthRepository _authRepository;

    public AuthSeeder(IAuthRepository authRepository)
    {
        _authRepository = authRepository;
    }

    public async Task SeedAsync()
    {
        await _authRepository.EnsureUserAsync(new UserAccount
        {
            Username = "demo_user",
            DisplayName = "Aarav Sharma",
            FullName = "Aarav Sharma",
            Email = "demo@lunchbox.local",
            Password = "LunchBox@123",
            Mobile = "+91 98765 43210",
            MobileNumber = "+91 98765 43210",
            DefaultPickupAddress = "42 Market Street, Hyderabad",
            Role = "customer",
            SecurityPin = "4821",
            LastIssuedPin = "4821"
        });

        await _authRepository.EnsureUserAsync(new UserAccount
        {
            Username = "user",
            DisplayName = "Guest User",
            FullName = "Guest User",
            Email = "user@lunchbox.local",
            Password = "user123",
            Mobile = "+91 91111 11111",
            MobileNumber = "+91 91111 11111",
            DefaultPickupAddress = "Guest Street, Hyderabad",
            Role = "customer",
            SecurityPin = "1234",
            LastIssuedPin = "1234"
        });

        await _authRepository.EnsureUserAsync(new UserAccount
        {
            Username = "admin_user",
            DisplayName = "LunchBox Admin",
            FullName = "LunchBox Admin",
            Email = "admin@lunchbox.local",
            Password = "Admin@123",
            Mobile = "+91 90000 00000",
            MobileNumber = "+91 90000 00000",
            DefaultPickupAddress = "LunchBox HQ, Hyderabad",
            Role = "admin",
            SecurityPin = "9999",
            LastIssuedPin = "9999"
        });

        var captains = Enumerable.Range(1, 5)
            .Select(index => new UserAccount
            {
                Username = $"captain{index}",
                DisplayName = $"Captain {index}",
                FullName = $"Captain{index}",
                Email = $"captain{index}@lunchbox.local",
                Password = $"Captain{index}@123",
                Mobile = $"+91 90000 10{index:000}",
                MobileNumber = $"+91 90000 10{index:000}",
                DefaultPickupAddress = $"Captain Hub {index}, Hyderabad",
                Role = "captain",
                SecurityPin = (7000 + index).ToString(),
                LastIssuedPin = (7000 + index).ToString()
            });

        var customers = Enumerable.Range(1, 8)
            .Select(index => new UserAccount
            {
                Username = $"customer{index}",
                DisplayName = $"Customer {index}",
                FullName = $"Customer{index}",
                Email = $"customer{index}@lunchbox.local",
                Password = $"Customer{index}@123",
                Mobile = $"+91 91000 20{index:000}",
                MobileNumber = $"+91 91000 20{index:000}",
                DefaultPickupAddress = $"Customer Street {index}, Hyderabad",
                Role = "customer",
                SecurityPin = (5000 + index).ToString(),
                LastIssuedPin = (5000 + index).ToString()
            });

        foreach (var captain in captains)
        {
            await _authRepository.EnsureUserAsync(captain);
        }

        foreach (var customer in customers)
        {
            await _authRepository.EnsureUserAsync(customer);
        }
    }
}
