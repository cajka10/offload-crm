using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Infrastructure.Persistence;

namespace Offload.Crm.Infrastructure.Seeding;

/// <summary>
/// Realistic demo data for Development (D-09): 2 extra users, 8 customers, contacts, 12 projects across all
/// statuses, ~40 tasks (some overdue) and ~30 activities. Runs only when there are no customers yet.
/// </summary>
internal sealed class DemoDataSeeder(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    SeedOptions options,
    TimeProvider timeProvider,
    ILogger logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Customers.AnyAsync(ct))
        {
            return;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

        var admin = await userManager.FindByEmailAsync(options.AdminEmail)
            ?? throw new InvalidOperationException("Admin user must be seeded before demo data.");
        var jana = await EnsureUserAsync("jana.novakova@offload.local", "Jana Nováková");
        var petr = await EnsureUserAsync("petr.dvorak@offload.local", "Petr Dvořák");
        ApplicationUser[] users = [admin, jana, petr];

        var statuses = await db.ProjectStatuses.OrderBy(s => s.SortOrder).ToListAsync(ct);

        var customers = new List<Customer>
        {
            Customer("Stavby Řehoř s.r.o.", "25596641", "CZ25596641", CustomerStatus.Active, "info@stavbyrehor.cz", "+420 541 123 456", "https://stavbyrehor.cz", "Údolní 12", "Brno", "602 00"),
            Customer("Žluťoučký kůň a.s.", "45274649", "CZ45274649", CustomerStatus.Active, "obchod@zlutykun.cz", "+420 224 555 010", "https://zlutykun.cz", "Vinohradská 48", "Praha", "120 00"),
            Customer("Průmyslové služby Ústí s.r.o.", "27082440", "CZ27082440", CustomerStatus.Active, "kontakt@psusti.cz", "+420 475 200 300", "https://psusti.cz", "Průmyslová 5", "Ústí nad Labem", "400 01"),
            Customer("Nová Pekárna Kolín", "08812345", null, CustomerStatus.Lead, "pekarna@kolin-pecivo.cz", "+420 321 700 800", null, "Pražská 101", "Kolín", "280 02"),
            Customer("Česká logistika a.s.", "63998530", "CZ63998530", CustomerStatus.Active, "dispecink@ceskalogistika.cz", "+420 596 111 222", "https://ceskalogistika.cz", "Místecká 20", "Ostrava", "702 00"),
            Customer("Zelená energie s.r.o.", "04711122", "CZ04711122", CustomerStatus.Inactive, "info@zelenaenergie.cz", "+420 377 400 500", "https://zelenaenergie.cz", "Americká 3", "Plzeň", "301 00"),
            Customer("Moravské vinařství Šťastný", "12345678", null, CustomerStatus.Lead, "vino@stastny.cz", "+420 519 300 400", null, "Sklepní 7", "Mikulov", "692 01"),
            Customer("TechnoSoft CZ s.r.o.", "29031125", "CZ29031125", CustomerStatus.Active, "sales@technosoft.cz", "+420 603 987 654", "https://technosoft.cz", "Technická 2", "Praha", "160 00"),
        };
        db.Customers.AddRange(customers);

        string[][] contactNames =
        [
            ["Tomáš", "Řehoř", "Jednatel"], ["Lenka", "Malá", "Nákup"],
            ["Martin", "Kůň", "Ředitel IT"], ["Eva", "Svobodová", "Projektová manažerka"], ["Jiří", "Černý", "CFO"],
            ["Pavel", "Horák", "Vedoucí údržby"],
            ["Marie", "Pekařová", "Majitelka"],
            ["Ondřej", "Veselý", "Logistika"], ["Kateřina", "Zelená", "Asistentka"],
            ["Radek", "Beneš", "Technický ředitel"],
            ["Zdeněk", "Šťastný", "Majitel"],
            ["Lucie", "Procházková", "Product owner"], ["Michal", "Kučera", "CTO"],
        ];
        int[] contactCustomer = [0, 0, 1, 1, 1, 2, 3, 4, 4, 5, 6, 7, 7];
        for (var i = 0; i < contactNames.Length; i++)
        {
            var (first, last, position) = (contactNames[i][0], contactNames[i][1], contactNames[i][2]);
            customers[contactCustomer[i]].Contacts.Add(new Contact
            {
                FirstName = first,
                LastName = last,
                Position = position,
                Email = $"{Ascii(first)}.{Ascii(last)}@example.cz".ToLowerInvariant(),
                Phone = $"+420 60{i % 10} {100 + (i * 37 % 900)} {200 + (i * 53 % 700)}",
            });
        }

        // 12 projects covering every status and priority. Customers 3 (Nová Pekárna) and 6 (vinařství) have none.
        (string Name, int Customer, int Status, Priority Priority, int StartOffset, int TargetOffset, decimal? Value)[] projectSpecs =
        [
            ("Nový firemní web", 0, 3, Priority.High, -60, 20, 450_000m),
            ("Rozpočtový nástroj stavby", 0, 0, Priority.Medium, 0, 90, null),
            ("E-shop Žluťoučký kůň", 1, 4, Priority.Critical, -120, -5, 1_250_000m),
            ("Věrnostní program", 1, 1, Priority.Low, -10, 60, 300_000m),
            ("Mobilní aplikace pro obchodníky", 1, 2, Priority.Medium, -5, 45, 800_000m),
            ("Evidence údržby strojů", 2, 5, Priority.Medium, -200, -30, 520_000m),
            ("Servisní smlouva — údržba", 2, 6, Priority.Low, -30, 335, 120_000m),
            ("Sledování zásilek", 4, 3, Priority.High, -45, 10, 980_000m),
            ("Integrace s ERP", 4, 1, Priority.Critical, -3, 7, 650_000m),
            ("Portál pro zákazníky", 5, 7, Priority.Low, -150, -60, 400_000m),
            ("Migrace do cloudu", 7, 4, Priority.High, -90, 3, 1_500_000m),
            ("Interní CRM rozšíření", 7, 0, Priority.Medium, 5, 120, 200_000m),
        ];
        var projects = new List<Project>();
        for (var i = 0; i < projectSpecs.Length; i++)
        {
            var spec = projectSpecs[i];
            var status = statuses[spec.Status];
            var project = new Project
            {
                Name = spec.Name,
                Description = $"Demo projekt: {spec.Name}.",
                Customer = customers[spec.Customer],
                Status = status,
                Priority = spec.Priority,
                ResponsibleUserId = users[i % users.Length].Id,
                StartDate = today.AddDays(spec.StartOffset),
                TargetDate = today.AddDays(spec.TargetOffset),
                EstimatedValue = spec.Value,
                ActualValue = status.Name == "Delivered" ? spec.Value * 1.05m : null,
            };
            projects.Add(project);
        }

        db.Projects.AddRange(projects);

        string[] taskTitles =
        [
            "Úvodní schůzka s klientem", "Sepsat požadavky", "Návrh databáze", "Grafický návrh",
            "Implementace přihlášení", "Napojení platební brány", "Testování na stagingu", "Nasazení do produkce",
            "Školení uživatelů", "Předávací protokol",
        ];
        var taskStatuses = Enum.GetValues<TaskItemStatus>();
        var priorities = Enum.GetValues<Priority>();
        var taskCount = 0;
        for (var p = 0; p < projects.Count; p++)
        {
            var project = projects[p];
            var perProject = p % 3 == 0 ? 4 : 3;
            for (var t = 0; t < perProject; t++)
            {
                var n = taskCount++;
                var status = project.Status.IsClosed ? TaskItemStatus.Done : taskStatuses[(n + t) % taskStatuses.Length];

                // Every 5th open task is overdue.
                var dueOffset = n % 5 == 0 ? -(3 + (n % 7)) : 2 + (n * 3 % 30);
                var task = new TaskItem
                {
                    Title = taskTitles[(p + t * 3) % taskTitles.Length],
                    Description = n % 2 == 0 ? "Automaticky vytvořený demo úkol." : null,
                    Project = project,
                    Priority = priorities[n % priorities.Length],
                    AssignedUserId = n % 6 == 5 ? null : users[n % users.Length].Id,
                    DueDate = n % 8 == 7 ? null : today.AddDays(dueOffset),
                };
                task.ChangeStatus(status, nowUtc.AddDays(-(n % 20)));
                db.Tasks.Add(task);
            }
        }

        var activityTypes = Enum.GetValues<ActivityType>();
        string[] activityTexts =
        [
            "Telefonát ohledně rozsahu projektu.", "Zaslána cenová nabídka e-mailem.", "Schůzka u klienta, prezentace návrhu.",
            "Interní poznámka: klient preferuje dodávku po etapách.", "Odsouhlasen harmonogram.", "Klient žádá úpravu rozpočtu.",
        ];
        for (var i = 0; i < 30; i++)
        {
            var type = activityTypes[i % activityTypes.Length];
            var activity = new Activity
            {
                Type = type,
                Date = nowUtc.AddDays(-(i * 2 + 1)).AddHours(-(i % 5)),
                Subject = i % 3 == 0 ? "Follow-up" : null,
                Description = activityTexts[i % activityTexts.Length],
                AuthorId = users[i % users.Length].Id,
            };

            if (i % 4 == 3)
            {
                // Customer-level activity (including customers without projects).
                activity.Customer = customers[i % customers.Count];
            }
            else
            {
                var project = projects[i % projects.Count];
                activity.Project = project;
                activity.Customer = project.Customer;
            }

            db.Activities.Add(activity);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Seeded demo data: {Customers} customers, {Projects} projects, {Tasks} tasks, 30 activities",
            customers.Count,
            projects.Count,
            taskCount);
    }

    private async Task<ApplicationUser> EnsureUserAsync(string email, string fullName)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            return user;
        }

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            IsActive = true,
        };
        DbInitializer.EnsureSucceeded(await userManager.CreateAsync(user, options.DemoUserPassword), $"create {email}");
        // D-27: demo users are ordinary users (no Admin role); only the seeded admin manages the app.
        return user;
    }

    private static Customer Customer(
        string name, string regNo, string? vat, CustomerStatus status, string email, string phone,
        string? web, string street, string city, string postal) => new()
        {
            CompanyName = name,
            RegistrationNo = regNo,
            VatId = vat,
            Status = status,
            Email = email,
            Phone = phone,
            Website = web,
            Street = street,
            City = city,
            PostalCode = postal,
            Country = Domain.Entities.Customer.DefaultCountry,
        };

    private static string Ascii(string s)
    {
        var normalized = s.Normalize(System.Text.NormalizationForm.FormD);
        return string.Concat(normalized.Where(c =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark));
    }
}
