using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Domain.ValueObjects;
using DistributionSystem.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Infrastructure.Data;

public static class SeedData
{
    public static async Task EnsureSuperAdminAsync(
        ApplicationDbContext context,
        string username,
        string email,
        string password,
        string phoneNumber,
        string fullName,
        string department)
    {
        var now = DateTime.UtcNow;
        var safeUsername = string.IsNullOrWhiteSpace(username) ? email : username;

        var superAdmin = await context.Users.FirstOrDefaultAsync(u => u.Role == UserRole.SuperAdmin);

        if (superAdmin is null)
        {
            superAdmin = await context.Users.FirstOrDefaultAsync(u => u.Email == email || u.Username == safeUsername);
        }

        if (superAdmin is null)
        {
            superAdmin = CreateUser(safeUsername, email, password, phoneNumber, UserRole.SuperAdmin, now);
            context.Users.Add(superAdmin);
        }
        else
        {
            superAdmin.Username = safeUsername;
            superAdmin.Email = email;
            superAdmin.PhoneNumber = phoneNumber;
            superAdmin.Role = UserRole.SuperAdmin;
            superAdmin.IsActive = true;
            superAdmin.MustChangePassword = false;
            superAdmin.UpdatedAt = now;
        }

        // Keep production credentials deterministic when superadmin seeding is enabled.
        // This ensures the configured username/password can always log in after deploy.
        superAdmin.PasswordHash = PasswordHasher.Hash(password);
        superAdmin.Role = UserRole.SuperAdmin;
        superAdmin.IsActive = true;
        superAdmin.MustChangePassword = false;
        superAdmin.FailedLoginAttempts = 0;
        superAdmin.LockoutEnd = null;
        superAdmin.RefreshToken = null;
        superAdmin.RefreshTokenExpiryTime = null;
        superAdmin.UpdatedAt = now;

        var adminProfile = await context.AdminProfiles.FirstOrDefaultAsync(a => a.UserId == superAdmin.Id);
        if (adminProfile is null)
        {
            context.AdminProfiles.Add(new AdminProfile
            {
                Id = Guid.NewGuid(),
                UserId = superAdmin.Id,
                FullName = fullName,
                Department = department,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            adminProfile.FullName = fullName;
            adminProfile.Department = department;
            adminProfile.UpdatedAt = now;
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds demo data for local/dev use. <paramref name="superAdminSeedPassword"/> comes from the
    /// SuperAdminSeed:Password configuration key (User Secrets locally, SuperAdminSeed__Password in
    /// production) — never hardcode a default here, since this path can create or reset a SuperAdmin
    /// login when DatabaseSettings:SeedDataOnStartup is enabled.
    /// </summary>
    public static async Task InitializeAsync(ApplicationDbContext context, string? superAdminSeedPassword)
    {
        var now = DateTime.UtcNow;

        var hasAdmin = await context.Users.AnyAsync(u => u.Role == UserRole.Admin);
        var hasSuperAdmin = await context.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin);

        if (hasAdmin)
        {
            if (string.IsNullOrWhiteSpace(superAdminSeedPassword))
            {
                throw new InvalidOperationException(
                    "DatabaseSettings:SeedDataOnStartup is enabled and a SuperAdmin account needs to be " +
                    "created or reset, but SuperAdminSeed:Password is not configured. Set it via User " +
                    "Secrets locally or the SuperAdminSeed__Password environment variable.");
            }

            if (!hasSuperAdmin)
            {
                var superAdminUser = CreateUser("superadmin", "superadmin@janasiri.lk", superAdminSeedPassword, "+94770000001", UserRole.SuperAdmin, now);
                context.Users.Add(superAdminUser);
                context.AdminProfiles.Add(new AdminProfile
                {
                    Id = Guid.NewGuid(),
                    UserId = superAdminUser.Id,
                    FullName = "Super Administrator",
                    Department = "Executive",
                    CreatedAt = now
                });

                await context.SaveChangesAsync();
            }
            else
            {
                var existingSuperAdmin = await context.Users.FirstAsync(u => u.Role == UserRole.SuperAdmin);
                // Keep seed credentials deterministic for local login, sourced from configuration.
                existingSuperAdmin.PasswordHash = PasswordHasher.Hash(superAdminSeedPassword);
                existingSuperAdmin.IsActive = true;

                await context.SaveChangesAsync();
            }

            return;
        }

        // ──────────────────────────────────────────────────────────────────
        // 0. REGIONS & SUB-REGIONS
        // ──────────────────────────────────────────────────────────────────
        var regionWestern = new Region { Id = Guid.NewGuid(), Name = "Western Province", IsActive = true, CreatedAt = now };
        var regionSouthern = new Region { Id = Guid.NewGuid(), Name = "Southern Province", IsActive = true, CreatedAt = now };
        var regionCentral = new Region { Id = Guid.NewGuid(), Name = "Central Province", IsActive = true, CreatedAt = now };
        context.Set<Region>().AddRange(regionWestern, regionSouthern, regionCentral);

        var subColombo = new SubRegion { Id = Guid.NewGuid(), Name = "Colombo District", RegionId = regionWestern.Id, IsActive = true, CreatedAt = now };
        var subGampaha = new SubRegion { Id = Guid.NewGuid(), Name = "Gampaha District", RegionId = regionWestern.Id, IsActive = true, CreatedAt = now };
        var subKalutara = new SubRegion { Id = Guid.NewGuid(), Name = "Kalutara District", RegionId = regionWestern.Id, IsActive = true, CreatedAt = now };
        var subGalle = new SubRegion { Id = Guid.NewGuid(), Name = "Galle District", RegionId = regionSouthern.Id, IsActive = true, CreatedAt = now };
        var subMatara = new SubRegion { Id = Guid.NewGuid(), Name = "Matara District", RegionId = regionSouthern.Id, IsActive = true, CreatedAt = now };
        var subKandy = new SubRegion { Id = Guid.NewGuid(), Name = "Kandy District", RegionId = regionCentral.Id, IsActive = true, CreatedAt = now };
        context.Set<SubRegion>().AddRange(subColombo, subGampaha, subKalutara, subGalle, subMatara, subKandy);

        // ──────────────────────────────────────────────────────────────────
        // 1. USERS + PROFILES
        // ──────────────────────────────────────────────────────────────────

        // --- Admin ---
        var adminUser = CreateUser("admin", "admin@janasiri.lk", "Admin@123", "+94771234567", UserRole.Admin, now);
        context.Users.Add(adminUser);

        var adminProfile = new AdminProfile
        {
            Id = Guid.NewGuid(), UserId = adminUser.Id,
            FullName = "System Administrator", Department = "Management", CreatedAt = now
        };
        context.AdminProfiles.Add(adminProfile);

        if (!hasSuperAdmin)
        {
            var superAdminUser = CreateUser("superadmin", "superadmin@janasiri.lk", "SuperAdmin@123", "+94770000001", UserRole.SuperAdmin, now);
            context.Users.Add(superAdminUser);
            context.AdminProfiles.Add(new AdminProfile
            {
                Id = Guid.NewGuid(),
                UserId = superAdminUser.Id,
                FullName = "Super Administrator",
                Department = "Executive",
                CreatedAt = now
            });
        }

        // --- Coordinators ---
        var coordUser1 = CreateUser("coord.nimal", "coord.nimal@janasiri.lk", "Coord@123", "+94779876543", UserRole.SalesCoordinator, now);
        var coordUser2 = CreateUser("coord.sanduni", "coord.sanduni@janasiri.lk", "Coord@123", "+94779876544", UserRole.SalesCoordinator, now);
        context.Users.AddRange(coordUser1, coordUser2);

        var coord1 = new CoordinatorProfile
        {
            Id = Guid.NewGuid(), UserId = coordUser1.Id, FullName = "Nimal Jayasinghe",
            EmployeeCode = "COORD001", RegionId = regionWestern.Id,
            HireDate = new DateTime(2022, 6, 1, 0, 0, 0, DateTimeKind.Utc), CreatedAt = now
        };
        var coord2 = new CoordinatorProfile
        {
            Id = Guid.NewGuid(), UserId = coordUser2.Id, FullName = "Sanduni Fernando",
            EmployeeCode = "COORD002", RegionId = regionSouthern.Id,
            HireDate = new DateTime(2023, 3, 15, 0, 0, 0, DateTimeKind.Utc), CreatedAt = now
        };
        context.CoordinatorProfiles.AddRange(coord1, coord2);

        // --- Sales Reps ---
        var repUser1 = CreateUser("rep.kamal", "kamal@janasiri.lk", "Rep@123", "+94772345678", UserRole.SalesRep, now);
        var repUser2 = CreateUser("rep.nimal", "nimal@janasiri.lk", "Rep@123", "+94773456789", UserRole.SalesRep, now);
        var repUser3 = CreateUser("rep.sunil", "sunil@janasiri.lk", "Rep@123", "+94774567891", UserRole.SalesRep, now);
        context.Users.AddRange(repUser1, repUser2, repUser3);

        var rep1 = new SalesRepProfile
        {
            Id = Guid.NewGuid(), UserId = repUser1.Id, FullName = "Kamal Perera",
            EmployeeCode = "REP001", HireDate = new DateTime(2023, 1, 15, 0, 0, 0, DateTimeKind.Utc), CreatedAt = now
        };
        rep1.Regions.Add(new RepRegion { RepId = rep1.Id, RegionId = regionWestern.Id });
        rep1.SubRegions.Add(new RepSubRegion { RepId = rep1.Id, SubRegionId = subColombo.Id });
        rep1.Coordinators.Add(new RepCoordinator { RepId = rep1.Id, CoordinatorId = coord1.Id });

        var rep2 = new SalesRepProfile
        {
            Id = Guid.NewGuid(), UserId = repUser2.Id, FullName = "Nimal Silva",
            EmployeeCode = "REP002", HireDate = new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc), CreatedAt = now
        };
        rep2.Regions.Add(new RepRegion { RepId = rep2.Id, RegionId = regionWestern.Id });
        rep2.SubRegions.Add(new RepSubRegion { RepId = rep2.Id, SubRegionId = subGampaha.Id });
        rep2.Coordinators.Add(new RepCoordinator { RepId = rep2.Id, CoordinatorId = coord1.Id });

        var rep3 = new SalesRepProfile
        {
            Id = Guid.NewGuid(), UserId = repUser3.Id, FullName = "Sunil Bandara",
            EmployeeCode = "REP003", HireDate = new DateTime(2024, 2, 10, 0, 0, 0, DateTimeKind.Utc), CreatedAt = now
        };
        rep3.Regions.Add(new RepRegion { RepId = rep3.Id, RegionId = regionSouthern.Id });
        rep3.SubRegions.Add(new RepSubRegion { RepId = rep3.Id, SubRegionId = subGalle.Id });
        rep3.Coordinators.Add(new RepCoordinator { RepId = rep3.Id, CoordinatorId = coord2.Id });

        context.SalesRepProfiles.AddRange(rep1, rep2, rep3);

        // --- Customers (6) ---
        var custUser1 = CreateUser("shop.laksiri", "laksiri@gmail.com", "Cust@123", "+94774567890", UserRole.Customer, now);
        var custUser2 = CreateUser("shop.saman", "saman@gmail.com", "Cust@123", "+94775678901", UserRole.Customer, now);
        var custUser3 = CreateUser("shop.ruwan", "ruwan@gmail.com", "Cust@123", "+94776789012", UserRole.Customer, now);
        var custUser4 = CreateUser("shop.kumari", "kumari@gmail.com", "Cust@123", "+94777890123", UserRole.Customer, now);
        var custUser5 = CreateUser("shop.anura", "anura@gmail.com", "Cust@123", "+94778901234", UserRole.Customer, now);
        var custUser6 = CreateUser("shop.dilshan", "dilshan@gmail.com", "Cust@123", "+94779012345", UserRole.Customer, now);
        context.Users.AddRange(custUser1, custUser2, custUser3, custUser4, custUser5, custUser6);

        var cust1 = new CustomerProfile
        {
            Id = Guid.NewGuid(), UserId = custUser1.Id, ShopName = "Laksiri Grocery",
            BusinessRegistrationNumber = "BR-2024-001",
            AssignedRepId = rep1.Id,
            AssignedCoordinatorId = coord1.Id, RegionId = regionWestern.Id, SubRegionId = subColombo.Id,
            ApprovalStatus = CustomerApprovalStatus.Approved,
            ApprovedByCoordinatorId = coord1.Id, ApprovedAt = now.AddMonths(-6),
            Address = new Address { Street = "123 Galle Road", City = "Colombo 4", State = "Western", PostalCode = "00400" },
            Location = new Location { Latitude = 6.8869, Longitude = 79.8614 }, CreatedAt = now.AddMonths(-6)
        };
        var cust2 = new CustomerProfile
        {
            Id = Guid.NewGuid(), UserId = custUser2.Id, ShopName = "Saman Stores",
            BusinessRegistrationNumber = "BR-2024-002",
            AssignedRepId = rep1.Id,
            AssignedCoordinatorId = coord1.Id, RegionId = regionWestern.Id, SubRegionId = subColombo.Id,
            ApprovalStatus = CustomerApprovalStatus.Approved,
            ApprovedByCoordinatorId = coord1.Id, ApprovedAt = now.AddMonths(-5),
            Address = new Address { Street = "45 Kandy Road", City = "Kadawatha", State = "Western", PostalCode = "11850" },
            Location = new Location { Latitude = 6.9930, Longitude = 79.9520 }, CreatedAt = now.AddMonths(-5)
        };
        var cust3 = new CustomerProfile
        {
            Id = Guid.NewGuid(), UserId = custUser3.Id, ShopName = "Ruwan Minimart",
            BusinessRegistrationNumber = "BR-2024-003",
            AssignedRepId = rep2.Id,
            AssignedCoordinatorId = coord1.Id, RegionId = regionWestern.Id, SubRegionId = subGampaha.Id,
            ApprovalStatus = CustomerApprovalStatus.Approved,
            ApprovedByCoordinatorId = coord1.Id, ApprovedAt = now.AddMonths(-4),
            Address = new Address { Street = "78 Negombo Road", City = "Ja-Ela", State = "Western", PostalCode = "11350" },
            Location = new Location { Latitude = 7.0752, Longitude = 79.8914 }, CreatedAt = now.AddMonths(-4)
        };
        var cust4 = new CustomerProfile
        {
            Id = Guid.NewGuid(), UserId = custUser4.Id, ShopName = "Kumari Super",
            BusinessRegistrationNumber = "BR-2024-004",
            AssignedRepId = rep2.Id,
            AssignedCoordinatorId = coord1.Id, RegionId = regionWestern.Id, SubRegionId = subGampaha.Id,
            ApprovalStatus = CustomerApprovalStatus.Approved,
            ApprovedByCoordinatorId = coord1.Id, ApprovedAt = now.AddMonths(-3),
            Address = new Address { Street = "12 Baseline Road", City = "Colombo 9", State = "Western", PostalCode = "00900" },
            Location = new Location { Latitude = 6.9271, Longitude = 79.8612 }, CreatedAt = now.AddMonths(-3)
        };
        var cust5 = new CustomerProfile
        {
            Id = Guid.NewGuid(), UserId = custUser5.Id, ShopName = "Anura Wholesale",
            BusinessRegistrationNumber = "BR-2024-005",
            AssignedRepId = rep3.Id,
            AssignedCoordinatorId = coord2.Id, RegionId = regionSouthern.Id, SubRegionId = subGalle.Id,
            ApprovalStatus = CustomerApprovalStatus.Approved,
            ApprovedByCoordinatorId = coord2.Id, ApprovedAt = now.AddMonths(-2),
            Address = new Address { Street = "56 Matara Road", City = "Galle", State = "Southern", PostalCode = "80000" },
            Location = new Location { Latitude = 6.0535, Longitude = 80.2210 }, CreatedAt = now.AddMonths(-2)
        };
        var cust6 = new CustomerProfile
        {
            Id = Guid.NewGuid(), UserId = custUser6.Id, ShopName = "Dilshan Corner Shop",
            BusinessRegistrationNumber = "BR-2024-006",
            AssignedRepId = rep3.Id,
            AssignedCoordinatorId = coord2.Id, RegionId = regionSouthern.Id, SubRegionId = subGalle.Id,
            ApprovalStatus = CustomerApprovalStatus.PendingApproval,
            Address = new Address { Street = "99 Wakwella Road", City = "Galle", State = "Southern", PostalCode = "80000" },
            Location = new Location { Latitude = 6.0450, Longitude = 80.2170 }, CreatedAt = now.AddDays(-3)
        };
        context.CustomerProfiles.AddRange(cust1, cust2, cust3, cust4, cust5, cust6);

        // ──────────────────────────────────────────────────────────────────
        // 2. CATEGORIES
        // ──────────────────────────────────────────────────────────────────
        var catBeverages = new Category { Id = Guid.NewGuid(), Name = "Beverages", Description = "Soft drinks, juices, and water", SortOrder = 1, IsActive = true, CreatedAt = now };
        var catSnacks    = new Category { Id = Guid.NewGuid(), Name = "Snacks", Description = "Chips, biscuits, and snack items", SortOrder = 2, IsActive = true, CreatedAt = now };
        var catDairy     = new Category { Id = Guid.NewGuid(), Name = "Dairy Products", Description = "Milk, yogurt, cheese", SortOrder = 3, IsActive = true, CreatedAt = now };
        var catRice      = new Category { Id = Guid.NewGuid(), Name = "Rice & Grains", Description = "Rice, flour, and grain products", SortOrder = 4, IsActive = true, CreatedAt = now };
        var catCare      = new Category { Id = Guid.NewGuid(), Name = "Personal Care", Description = "Soap, shampoo, and personal hygiene", SortOrder = 5, IsActive = true, CreatedAt = now };
        var catCondiment = new Category { Id = Guid.NewGuid(), Name = "Condiments & Spices", Description = "Sauces, spices, and seasonings", SortOrder = 6, IsActive = true, CreatedAt = now };
        // example subcategories under beverages and snacks
        var catSoftDrinks = new Category { Id = Guid.NewGuid(), Name = "Soft Drinks", ParentCategoryId = catBeverages.Id, SortOrder = 1, IsActive = true, CreatedAt = now };
        var catJuices     = new Category { Id = Guid.NewGuid(), Name = "Juices", ParentCategoryId = catBeverages.Id, SortOrder = 2, IsActive = true, CreatedAt = now };
        var catChips      = new Category { Id = Guid.NewGuid(), Name = "Chips", ParentCategoryId = catSnacks.Id, SortOrder = 1, IsActive = true, CreatedAt = now };
        var catBiscuits   = new Category { Id = Guid.NewGuid(), Name = "Biscuits", ParentCategoryId = catSnacks.Id, SortOrder = 2, IsActive = true, CreatedAt = now };
        context.Categories.AddRange(catBeverages, catSnacks, catDairy, catRice, catCare, catCondiment, catSoftDrinks, catJuices, catChips, catBiscuits);

        // ──────────────────────────────────────────────────────────────────
        // 3. PRODUCTS  (20 realistic Sri Lankan distribution items)
        // ──────────────────────────────────────────────────────────────────
        var p1  = CreateProduct("Elephant House Cream Soda 1.5L",  "BEV-001", catSoftDrinks.Id, "Elephant House", 320, 320,   150, now);
        var p2  = CreateProduct("Elephant House Ginger Beer 1.5L", "BEV-002", catSoftDrinks.Id, "Elephant House", 320, 320,   120, now);
        var p3  = CreateProduct("MD Orange Juice 1L",              "BEV-003", catJuices.Id,      "MD",             280, 280,   200, now);
        var p4  = CreateProduct("Nestomalt 400g",                  "BEV-004", catBeverages.Id,   "Nestlé",         890, 890,    80, now);
        var p5  = CreateProduct("Munchee Cream Cracker 500g",      "SNK-001", catBiscuits.Id,    "Munchee",        350, 350,   300, now);
        var p6  = CreateProduct("Munchee Lemon Puff 200g",         "SNK-002", catBiscuits.Id,    "Munchee",        180, 180,   250, now);
        var p7  = CreateProduct("Maliban Gold Marie 400g",         "SNK-003", catSnacks.Id,     "Maliban",         280, 280,   180, now);
        var p8  = CreateProduct("Tipi Tip Chips 100g",             "SNK-004", catChips.Id,       "CBL",             120, 120,   400, now);
        var p9  = CreateProduct("Anchor Fresh Milk 1L",            "DRY-001", catDairy.Id,     "Fonterra",        395, 395,    90, now);
        var p10 = CreateProduct("Highland Yoghurt Strawberry 80g", "DRY-002", catDairy.Id,     "Milco",           75,  75,   500, now);
        var p11 = CreateProduct("Pelawatte Butter 200g",           "DRY-003", catDairy.Id,     "Pelawatte",       580, 580,    60, now);
        var p12 = CreateProduct("Nipuna White Rice 5kg",           "RCE-001", catRice.Id,      "CIC",            1250,1250,   100, now);
        var p13 = CreateProduct("MDK Red Raw Rice 5kg",            "RCE-002", catRice.Id,      "MDK",            1150,1150,   120, now);
        var p14 = CreateProduct("Prima Wheat Flour 1kg",           "RCE-003", catRice.Id,      "Prima",           245, 245,   200, now);
        var p15 = CreateProduct("Sunlight Soap Bar 110g",          "CAR-001", catCare.Id,      "Unilever",        95,  95,    350, now);
        var p16 = CreateProduct("Signal Toothpaste 120g",          "CAR-002", catCare.Id,      "Unilever",       320, 320,    160, now);
        var p17 = CreateProduct("Lifebuoy Handwash 200ml",         "CAR-003", catCare.Id,      "Unilever",       450, 450,    140, now);
        var p18 = CreateProduct("Knorr Chicken Cube 10g",          "CND-001", catCondiment.Id, "Unilever",        35,  35,    600, now);
        var p19 = CreateProduct("MD Soy Sauce 350ml",              "CND-002", catCondiment.Id, "MD",             195, 195,    180, now);
        var p20 = CreateProduct("Raigam Chilli Sauce 350ml",       "CND-003", catCondiment.Id, "Raigam",         210, 210,    160, now);
        context.Products.AddRange(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15, p16, p17, p18, p19, p20);

        // ──────────────────────────────────────────────────────────────────
        // 4. ROUTES    (3 routes across 2 territories)
        // ──────────────────────────────────────────────────────────────────
        var route1 = new Route
        {
            Id = Guid.NewGuid(), Name = "Colombo South Route", Description = "Colombo 3, 4, 5, 6 areas",
            DaysOfWeek = "Monday,Wednesday,Friday",
            EstimatedDurationMinutes = 360, IsActive = true, CreatedAt = now
        };
        var route2 = new Route
        {
            Id = Guid.NewGuid(), Name = "Gampaha Main Route", Description = "Kadawatha, Ja-Ela, Wattala areas",
            DaysOfWeek = "Tuesday,Thursday",
            EstimatedDurationMinutes = 420, IsActive = true, CreatedAt = now
        };
        var route3 = new Route
        {
            Id = Guid.NewGuid(), Name = "Galle Coastal Route", Description = "Galle Fort, Unawatuna, Hikkaduwa",
            DaysOfWeek = "Monday,Wednesday,Friday",
            EstimatedDurationMinutes = 300, IsActive = true, CreatedAt = now
        };
        context.Routes.AddRange(route1, route2, route3);

        context.Set<RepRoute>().AddRange(
            new RepRoute { Id = Guid.NewGuid(), RouteId = route1.Id, RepId = rep1.Id, CreatedAt = now },
            new RepRoute { Id = Guid.NewGuid(), RouteId = route2.Id, RepId = rep2.Id, CreatedAt = now },
            new RepRoute { Id = Guid.NewGuid(), RouteId = route3.Id, RepId = rep3.Id, CreatedAt = now }
        );

        // Route ↔ Customers
        context.RouteCustomers.AddRange(
            new RouteCustomer { Id = Guid.NewGuid(), RouteId = route1.Id, CustomerId = cust1.Id, VisitOrder = 1, VisitFrequency = "Weekly" },
            new RouteCustomer { Id = Guid.NewGuid(), RouteId = route1.Id, CustomerId = cust2.Id, VisitOrder = 2, VisitFrequency = "Weekly" },
            new RouteCustomer { Id = Guid.NewGuid(), RouteId = route2.Id, CustomerId = cust3.Id, VisitOrder = 1, VisitFrequency = "Weekly" },
            new RouteCustomer { Id = Guid.NewGuid(), RouteId = route2.Id, CustomerId = cust4.Id, VisitOrder = 2, VisitFrequency = "BiWeekly" },
            new RouteCustomer { Id = Guid.NewGuid(), RouteId = route3.Id, CustomerId = cust5.Id, VisitOrder = 1, VisitFrequency = "Weekly" }
        );

        // ──────────────────────────────────────────────────────────────────
        // 5. ORDERS   (15 orders in various statuses, spread over time)
        // ──────────────────────────────────────────────────────────────────

        // Helper: sequential order numbers
        int orderSeq = 1;
        string NextOrderNum() => $"ORD-2026-{orderSeq++:D4}";

        // -- Orders from 30+ days ago (older, completed) --
        var ord1 = CreateOrder(NextOrderNum(), cust1.Id, rep1.Id, OrderStatus.Completed, now.AddDays(-45), now.AddDays(-43), 8750, 0, 8750, now.AddDays(-45));
        var ord2 = CreateOrder(NextOrderNum(), cust2.Id, rep1.Id, OrderStatus.Completed, now.AddDays(-40), now.AddDays(-38), 12400, 620, 11780, now.AddDays(-40));
        var ord3 = CreateOrder(NextOrderNum(), cust5.Id, rep3.Id, OrderStatus.Delivered,  now.AddDays(-35), now.AddDays(-33), 24500, 1225, 23275, now.AddDays(-35));

        // -- Orders from 10-20 days ago --
        var ord4  = CreateOrder(NextOrderNum(), cust1.Id, rep1.Id, OrderStatus.Delivered,   now.AddDays(-18), now.AddDays(-16), 6400,  0,    6400,  now.AddDays(-18));
        var ord5  = CreateOrder(NextOrderNum(), cust3.Id, rep2.Id, OrderStatus.Completed,   now.AddDays(-15), now.AddDays(-13), 4200,  210,  3990,  now.AddDays(-15));
        var ord6  = CreateOrder(NextOrderNum(), cust4.Id, rep2.Id, OrderStatus.Dispatched,  now.AddDays(-12), null,             9800,  490,  9310,  now.AddDays(-12));
        var ord7  = CreateOrder(NextOrderNum(), cust2.Id, rep1.Id, OrderStatus.Processing,  now.AddDays(-10), null,             15600, 780,  14820, now.AddDays(-10));

        // -- Orders from last 7 days (recent, visible on dashboard) --
        var ord8  = CreateOrder(NextOrderNum(), cust5.Id, rep3.Id, OrderStatus.Approved,    now.AddDays(-6),  null,             18200, 910,  17290, now.AddDays(-6));
        var ord9  = CreateOrder(NextOrderNum(), cust1.Id, rep1.Id, OrderStatus.Pending,     now.AddDays(-4),  null,             5300,  0,    5300,  now.AddDays(-4));
        var ord10 = CreateOrder(NextOrderNum(), cust4.Id, rep2.Id, OrderStatus.Pending,     now.AddDays(-3),  null,             7800,  390,  7410,  now.AddDays(-3));
        var ord11 = CreateOrder(NextOrderNum(), cust3.Id, rep2.Id, OrderStatus.Pending,     now.AddDays(-2),  null,             3600,  0,    3600,  now.AddDays(-2));

        // -- Today's orders --
        var ord12 = CreateOrder(NextOrderNum(), cust2.Id, rep1.Id, OrderStatus.Pending,     now,              null,             11200, 560,  10640, now);
        var ord13 = CreateOrder(NextOrderNum(), cust5.Id, rep3.Id, OrderStatus.Pending,     now,              null,             8900,  445,  8455,  now);

        // -- Cancelled / Rejected (for realism) --
        var ord14 = CreateOrder(NextOrderNum(), cust3.Id, rep2.Id, OrderStatus.Cancelled,   now.AddDays(-25), null,             2800,  0,    2800,  now.AddDays(-25));
        ord14.CancellationReason = "Customer requested cancellation – duplicate order";
        var ord15 = CreateOrder(NextOrderNum(), cust6.Id, null,    OrderStatus.Rejected,    now.AddDays(-1),  null,             4500,  0,    4500,  now.AddDays(-1));
        ord15.RejectionReason = "Customer not yet approved";

        context.Orders.AddRange(ord1, ord2, ord3, ord4, ord5, ord6, ord7, ord8, ord9, ord10, ord11, ord12, ord13, ord14, ord15);

        // ──────────────────────────────────────────────────────────────────
        // 5b. ORDER ITEMS  (2-3 items per order for realism)
        // ──────────────────────────────────────────────────────────────────
        AddItems(context, ord1,  (p1, 10, 320), (p5, 15, 350));
        AddItems(context, ord2,  (p12, 5, 1250), (p14, 10, 245), (p18, 20, 35));
        AddItems(context, ord3,  (p9, 30, 395), (p11, 10, 580), (p4, 10, 890));
        AddItems(context, ord4,  (p1, 10, 320), (p2, 10, 320));
        AddItems(context, ord5,  (p6, 10, 180), (p7, 5, 280), (p8, 10, 120));
        AddItems(context, ord6,  (p12, 4, 1250), (p13, 4, 1150));
        AddItems(context, ord7,  (p9, 20, 395), (p10, 40, 75), (p15, 20, 95));
        AddItems(context, ord8,  (p4, 10, 890), (p16, 10, 320), (p17, 10, 450));
        AddItems(context, ord9,  (p5, 10, 350), (p6, 10, 180));
        AddItems(context, ord10, (p12, 3, 1250), (p14, 10, 245), (p19, 10, 195));
        AddItems(context, ord11, (p8, 30, 120));
        AddItems(context, ord12, (p1, 15, 320), (p3, 10, 280), (p18, 20, 35));
        AddItems(context, ord13, (p9, 10, 395), (p11, 5, 580), (p20, 10, 210));
        AddItems(context, ord14, (p15, 20, 95), (p16, 5, 320));
        AddItems(context, ord15, (p5, 10, 350), (p6, 5, 180));

        // ──────────────────────────────────────────────────────────────────
        // 6. PAYMENTS   (mix of statuses & methods)
        // ──────────────────────────────────────────────────────────────────
        context.Payments.AddRange(
            CreatePayment(cust1.Id, ord1.Id, 8750,  PaymentMethod.Cash,         PaymentStatus.Verified,  rep1.Id, adminUser.Id, "Full payment – cash",                       now.AddDays(-43)),
            CreatePayment(cust2.Id, ord2.Id, 11780, PaymentMethod.Cheque,       PaymentStatus.Verified,  rep1.Id, adminUser.Id, "Cheque #00234 – BOC Colombo",               now.AddDays(-38), chequeNo: "00234", bank: "BOC"),
            CreatePayment(cust5.Id, ord3.Id, 23275, PaymentMethod.BankTransfer, PaymentStatus.Verified,  rep3.Id, adminUser.Id, "Bank transfer ref TXN-88712",               now.AddDays(-33), refNo: "TXN-88712"),
            CreatePayment(cust1.Id, ord4.Id, 6400,  PaymentMethod.Cash,         PaymentStatus.Verified,  rep1.Id, adminUser.Id, "Cash collected on delivery",                now.AddDays(-16)),
            CreatePayment(cust3.Id, ord5.Id, 3990,  PaymentMethod.MobilePayment,PaymentStatus.Verified,  rep2.Id, adminUser.Id, "Via FriMi mobile wallet",                   now.AddDays(-13), refNo: "FRIMI-44521"),
            CreatePayment(cust4.Id, ord6.Id, 5000,  PaymentMethod.Cheque,       PaymentStatus.Pending,   rep2.Id, null,         "Partial cheque #00891 – HNB Kadawatha",     now.AddDays(-10), chequeNo: "00891", bank: "HNB"),
            CreatePayment(cust2.Id, ord7.Id, 14820, PaymentMethod.BankTransfer, PaymentStatus.Pending,   rep1.Id, null,         "Transfer pending verification",             now.AddDays(-8),  refNo: "TXN-90102"),
            CreatePayment(cust5.Id, null,    10000, PaymentMethod.Cash,         PaymentStatus.Pending,   rep3.Id, null,         "Advance payment, no specific order",        now.AddDays(-2)),
            CreatePayment(cust1.Id, ord9.Id, 5300,  PaymentMethod.Cash,         PaymentStatus.Pending,   rep1.Id, null,         "Collected with order",                      now.AddDays(-1))
        );

        // ──────────────────────────────────────────────────────────────────
        // 7. QUOTATIONS  (various statuses)
        // ──────────────────────────────────────────────────────────────────
        var qt1 = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QTN-1001", CustomerId = cust2.Id,
            RepId = rep1.Id, CoordinatorId = coord1.Id, Status = QuotationStatus.Approved,
            SubTotal = 15000, TaxAmount = 0, DiscountAmount = 750, TotalAmount = 14250,
            ValidUntil = now.AddDays(15), CreatedAt = now.AddDays(-10)
        };
        var qt2 = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QTN-1002", CustomerId = cust3.Id,
            RepId = rep2.Id, CoordinatorId = coord1.Id, Status = QuotationStatus.Submitted,
            SubTotal = 8400, TaxAmount = 0, DiscountAmount = 0, TotalAmount = 8400,
            ValidUntil = now.AddDays(20), CreatedAt = now.AddDays(-5)
        };
        var qt3 = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QTN-1003", CustomerId = cust5.Id,
            RepId = rep3.Id, CoordinatorId = coord2.Id, Status = QuotationStatus.Draft,
            SubTotal = 22000, TaxAmount = 0, DiscountAmount = 1100, TotalAmount = 20900,
            ValidUntil = now.AddDays(30), CreatedAt = now.AddDays(-2)
        };
        var qt4 = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QTN-1004", CustomerId = cust1.Id,
            RepId = rep1.Id, CoordinatorId = coord1.Id, Status = QuotationStatus.Rejected,
            SubTotal = 4800, TaxAmount = 0, DiscountAmount = 0, TotalAmount = 4800,
            RejectionReason = "Price too high – customer wants 10% discount",
            ValidUntil = now.AddDays(-5), CreatedAt = now.AddDays(-20)
        };
        context.Quotations.AddRange(qt1, qt2, qt3, qt4);

        // Quotation items
        context.QuotationItems.AddRange(
            new QuotationItem { Id = Guid.NewGuid(), QuotationId = qt1.Id, ProductId = p12.Id, Quantity = 10, UnitPrice = 1250, LineTotal = 12500, CreatedAt = now.AddDays(-10) },
            new QuotationItem { Id = Guid.NewGuid(), QuotationId = qt1.Id, ProductId = p14.Id, Quantity = 10, UnitPrice = 245,  LineTotal = 2450,  CreatedAt = now.AddDays(-10) },
            new QuotationItem { Id = Guid.NewGuid(), QuotationId = qt2.Id, ProductId = p5.Id,  Quantity = 12, UnitPrice = 350,  LineTotal = 4200,  CreatedAt = now.AddDays(-5) },
            new QuotationItem { Id = Guid.NewGuid(), QuotationId = qt2.Id, ProductId = p6.Id,  Quantity = 12, UnitPrice = 180,  LineTotal = 2160,  CreatedAt = now.AddDays(-5) },
            new QuotationItem { Id = Guid.NewGuid(), QuotationId = qt2.Id, ProductId = p7.Id,  Quantity = 7,  UnitPrice = 280,  LineTotal = 1960,  CreatedAt = now.AddDays(-5) },
            new QuotationItem { Id = Guid.NewGuid(), QuotationId = qt3.Id, ProductId = p4.Id,  Quantity = 15, UnitPrice = 890,  LineTotal = 13350, CreatedAt = now.AddDays(-2) },
            new QuotationItem { Id = Guid.NewGuid(), QuotationId = qt3.Id, ProductId = p9.Id,  Quantity = 20, UnitPrice = 395,  LineTotal = 7900,  CreatedAt = now.AddDays(-2) },
            new QuotationItem { Id = Guid.NewGuid(), QuotationId = qt4.Id, ProductId = p1.Id,  Quantity = 15, UnitPrice = 320,  LineTotal = 4800,  CreatedAt = now.AddDays(-20) }
        );

        // ──────────────────────────────────────────────────────────────────
        // 8. VISITS   (recent visits for rep dashboards)
        // ──────────────────────────────────────────────────────────────────
        context.Visits.AddRange(
            CreateVisit(rep1.Id, cust1.Id, route1.Id, now.AddDays(-3), VisitStatus.Completed,  1, 8750,  now.AddDays(-3)),
            CreateVisit(rep1.Id, cust2.Id, route1.Id, now.AddDays(-3), VisitStatus.Completed,  1, 0,     now.AddDays(-3)),
            CreateVisit(rep2.Id, cust3.Id, route2.Id, now.AddDays(-2), VisitStatus.Completed,  1, 3990,  now.AddDays(-2)),
            CreateVisit(rep2.Id, cust4.Id, route2.Id, now.AddDays(-2), VisitStatus.Skipped,    0, 0,     now.AddDays(-2), "Shop closed"),
            CreateVisit(rep3.Id, cust5.Id, route3.Id, now.AddDays(-1), VisitStatus.Completed,  2, 10000, now.AddDays(-1)),
            CreateVisit(rep1.Id, cust1.Id, route1.Id, now,             VisitStatus.Planned,    0, 0,     now),
            CreateVisit(rep1.Id, cust2.Id, route1.Id, now,             VisitStatus.Planned,    0, 0,     now),
            CreateVisit(rep2.Id, cust3.Id, route2.Id, now,             VisitStatus.Planned,    0, 0,     now)
        );

        // ──────────────────────────────────────────────────────────────────
        // 9. SALES TARGETS
        // ──────────────────────────────────────────────────────────────────
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd   = monthStart.AddMonths(1).AddDays(-1);
        context.SalesTargets.AddRange(
            new SalesTarget { Id = Guid.NewGuid(), RepId = rep1.Id, TargetPeriod = "Monthly", StartDate = monthStart, EndDate = monthEnd, TargetAmount = 500000, AchievedAmount = 41740, Status = "Active", CreatedAt = now },
            new SalesTarget { Id = Guid.NewGuid(), RepId = rep2.Id, TargetPeriod = "Monthly", StartDate = monthStart, EndDate = monthEnd, TargetAmount = 350000, AchievedAmount = 24310, Status = "Active", CreatedAt = now },
            new SalesTarget { Id = Guid.NewGuid(), RepId = rep3.Id, TargetPeriod = "Monthly", StartDate = monthStart, EndDate = monthEnd, TargetAmount = 400000, AchievedAmount = 49020, Status = "Active", CreatedAt = now }
        );

        // ──────────────────────────────────────────────────────────────────
        // 10. COMPLAINTS
        // ──────────────────────────────────────────────────────────────────
        context.Complaints.AddRange(
            new Complaint { Id = Guid.NewGuid(), CustomerId = cust1.Id, OrderId = ord1.Id, Subject = "Damaged packaging", Description = "3 bottles of Cream Soda arrived with dented labels and leaking caps.", Priority = ComplaintPriority.Medium, Status = ComplaintStatus.Resolved, ResolvedBy = adminUser.Id, ResolvedAt = now.AddDays(-40), CreatedAt = now.AddDays(-43) },
            new Complaint { Id = Guid.NewGuid(), CustomerId = cust4.Id, OrderId = ord6.Id, Subject = "Late delivery", Description = "Order was supposed to arrive on Monday but it's now Thursday.", Priority = ComplaintPriority.High, Status = ComplaintStatus.InProgress, AssignedTo = rep2.Id, CreatedAt = now.AddDays(-8) },
            new Complaint { Id = Guid.NewGuid(), CustomerId = cust5.Id, Subject = "Wrong items received", Description = "Received Highland Yoghurt instead of Anchor Fresh Milk in last order.", Priority = ComplaintPriority.High, Status = ComplaintStatus.Open, CreatedAt = now.AddDays(-1) }
        );

        // ──────────────────────────────────────────────────────────────────
        // 11. NOTIFICATIONS  (a few for each role)
        // ──────────────────────────────────────────────────────────────────
        context.Notifications.AddRange(
            new Notification { Id = Guid.NewGuid(), UserId = adminUser.Id,  NotificationType = NotificationType.NewOrder,            Title = "New Order Received",   Message = $"Order {ord12.OrderNumber} placed by Saman Stores worth LKR 10,640.",  CreatedAt = now },
            new Notification { Id = Guid.NewGuid(), UserId = adminUser.Id,  NotificationType = NotificationType.CustomerRegistration, Title = "New Customer Registration", Message = "Dilshan Corner Shop has registered and is awaiting approval.", CreatedAt = now.AddDays(-3) },
            new Notification { Id = Guid.NewGuid(), UserId = coordUser1.Id, NotificationType = NotificationType.QuotationSubmitted,   Title = "Quotation Submitted",  Message = $"Quotation QTN-1002 submitted by Nimal Silva for Ruwan Minimart.",    CreatedAt = now.AddDays(-5) },
            new Notification { Id = Guid.NewGuid(), UserId = repUser1.Id,   NotificationType = NotificationType.OrderStatusUpdate,    Title = "Order Approved",       Message = $"Order {ord8.OrderNumber} has been approved and is ready for processing.", CreatedAt = now.AddDays(-5) },
            new Notification { Id = Guid.NewGuid(), UserId = repUser2.Id,   NotificationType = NotificationType.TargetAchievement,    Title = "Target Progress",      Message = "You have achieved 7% of your monthly sales target.",                  CreatedAt = now.AddDays(-2) },
            new Notification { Id = Guid.NewGuid(), UserId = custUser1.Id,  NotificationType = NotificationType.OrderStatusUpdate,    Title = "Order Delivered",     Message = $"Your order {ord4.OrderNumber} has been delivered successfully.",      CreatedAt = now.AddDays(-16) },
            new Notification { Id = Guid.NewGuid(), UserId = custUser5.Id,  NotificationType = NotificationType.PaymentReminder,      Title = "Payment Reminder",     Message = "You have an outstanding balance of LKR 22,000. Please arrange payment.", CreatedAt = now.AddDays(-1) }
        );

        // ──────────────────────────────────────────────────────────────────
        // 12. STOCK MOVEMENTS  (initial stock-in records)
        // ──────────────────────────────────────────────────────────────────
        var products = new[] { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15, p16, p17, p18, p19, p20 };
        foreach (var p in products)
        {
            context.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(), ProductId = p.Id, MovementType = MovementType.In,
                Quantity = p.Quantity, Reason = "Initial stock import",
                ReferenceNumber = "IMPORT-001", CreatedByUserId = adminUser.Id, CreatedAt = now.AddMonths(-1)
            });
        }

        // ──────────────────────────────────────────────────────────────────
        // 13. PROMOTIONS
        // ──────────────────────────────────────────────────────────────────
        context.Promotions.AddRange(
            new Promotion
            {
                Id = Guid.NewGuid(), Name = "March Beverage Blast", Description = "10% off all beverages",
                PromotionType = "Percentage", DiscountPercent = 10, StartDate = monthStart, EndDate = monthEnd,
                IsActive = true, CreatedAt = now.AddDays(-5)
            },
            new Promotion
            {
                Id = Guid.NewGuid(), Name = "Buy 5 Get 1 Munchee", Description = "Buy 5 packs of Cream Crackers, get 1 free",
                PromotionType = "BuyXGetY", BuyQuantity = 5, GetQuantity = 1,
                StartDate = monthStart, EndDate = monthEnd, IsActive = true, CreatedAt = now.AddDays(-5)
            }
        );

        // ──────────────────────────────────────────────────────────────────
        // 14. SYSTEM CONFIGURATION
        // ──────────────────────────────────────────────────────────────────
        if (!await context.SystemConfigurations.AnyAsync())
        {
            context.SystemConfigurations.Add(new SystemConfiguration
            {
                Id = Guid.NewGuid(), CompanyName = "Janasiri Distribution (Pvt) Ltd",
                CompanyLogo = "/logo.png",
                CompanyAddress = "No. 42, Dutugemunu Street, Colombo 06, Sri Lanka",
                CompanyPhone = "+94 11 234 5678", CompanyEmail = "info@janasiri.lk",
                TaxNumber = "TIN-20240001234", Currency = "LKR",
                BrandPrimaryColor = "#1E40AF", BrandSecondaryColor = "#F59E0B",
                RequireCustomerApproval = true, RequireQuotationApproval = true,
                DefaultPaymentTermsDays = 30, DefaultCreditLimit = 25000,
                CreatedAt = now
            });
        }

        // ──────────────────────────────────────────────────────────────────
        // SAVE
        // ──────────────────────────────────────────────────────────────────
        await context.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════════

    private static User CreateUser(string username, string email, string password, string phone, UserRole role, DateTime now) => new()
    {
        Id = Guid.NewGuid(), Username = username, Email = email,
        PasswordHash = PasswordHasher.Hash(password), PhoneNumber = phone,
        Role = role, IsActive = true, CreatedAt = now
    };

    private static Product CreateProduct(string name, string sku, Guid categoryId, string brand, decimal price, decimal? mrp, int qty, DateTime now) => new()
    {
        Id = Guid.NewGuid(), Name = name, SKU = sku, CategoryId = categoryId,
        Brand = brand, SellingPrice = price, MRP = mrp, Quantity = qty, CreatedAt = now
    };

    private static Order CreateOrder(string orderNumber, Guid customerId, Guid? repId, OrderStatus status,
        DateTime orderDate, DateTime? deliveredDate, decimal subTotal, decimal discount, decimal total, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(), OrderNumber = orderNumber, CustomerId = customerId, RepId = repId,
        Status = status, OrderDate = orderDate, ActualDeliveryDate = deliveredDate,
        SubTotal = subTotal, DiscountAmount = discount, TotalAmount = total, CreatedAt = createdAt
    };

    private static void AddItems(ApplicationDbContext ctx, Order order, params (Product product, int qty, decimal unitPrice)[] items)
    {
        foreach (var (product, qty, unitPrice) in items)
        {
            ctx.OrderItems.Add(new OrderItem
            {
                Id = Guid.NewGuid(), OrderId = order.Id, ProductId = product.Id,
                Quantity = qty, UnitPrice = unitPrice, LineTotal = qty * unitPrice
            });
        }
    }

    private static Payment CreatePayment(Guid customerId, Guid? orderId, decimal amount, PaymentMethod method,
        PaymentStatus status, Guid? collectedBy, Guid? verifiedBy, string notes, DateTime date,
        string? refNo = null, string? chequeNo = null, string? bank = null) => new()
    {
        Id = Guid.NewGuid(), CustomerId = customerId, OrderId = orderId,
        Amount = amount, PaymentMethod = method, Status = status,
        CollectedByRepId = collectedBy, VerifiedByAdminId = verifiedBy,
        ReferenceNumber = refNo, ChequeNumber = chequeNo, BankName = bank,
        Notes = notes, PaymentDate = date, CreatedAt = date
    };

    private static Visit CreateVisit(Guid repId, Guid customerId, Guid routeId, DateTime plannedDate,
        VisitStatus status, int ordersPlaced, decimal paymentsCollected, DateTime createdAt, string? notes = null)
    {
        var v = new Visit
        {
            Id = Guid.NewGuid(), RepId = repId, CustomerId = customerId, RouteId = routeId,
            PlannedDate = plannedDate, Status = status, OrdersPlaced = ordersPlaced,
            PaymentsCollected = paymentsCollected, Notes = notes, CreatedAt = createdAt
        };
        if (status == VisitStatus.Completed || status == VisitStatus.CheckedOut)
        {
            v.ActualStartTime = plannedDate.AddHours(8);
            v.ActualEndTime = plannedDate.AddHours(8).AddMinutes(45);
        }
        return v;
    }
}
