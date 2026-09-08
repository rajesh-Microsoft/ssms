using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Models;
using SMMS.Api.Services;
using Xunit;

namespace SMMS.Api.Tests;

/// <summary>
/// Covers how access is worked out: the Owner/Tenant baseline that follows the flat's occupancy,
/// plus any committee positions the person holds.
/// </summary>
public class UserGroupPermissionTests : IDisposable
{
    private readonly List<SqliteConnection> _connections = [];

    private SmmsDbContext NewSociety(bool seedOccupancyGroups = true)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        _connections.Add(connection);
        var options = new DbContextOptionsBuilder<SmmsDbContext>().UseSqlite(connection).Options;
        var db = new SmmsDbContext(options);
        db.Database.EnsureCreated();

        if (seedOccupancyGroups)
        {
            AddGroup(db, UserGroups.Tenant, new()
            {
                [PermissionModules.Collections] = "None",
                [PermissionModules.Expenses] = "None",
                [PermissionModules.Income] = "None",
                [PermissionModules.Liabilities] = "None",
                [PermissionModules.Budgets] = "None",
                [PermissionModules.Inventory] = "None",
                [PermissionModules.Members] = "None",
                [PermissionModules.Settings] = "None",
                [PermissionModules.Complaints] = "View"
            });
            AddGroup(db, UserGroups.Owner, new()
            {
                [PermissionModules.Collections] = "View",
                [PermissionModules.Expenses] = "View",
                [PermissionModules.Income] = "View",
                [PermissionModules.Liabilities] = "View",
                [PermissionModules.Budgets] = "View",
                [PermissionModules.Inventory] = "View",
                [PermissionModules.Members] = "View",
                [PermissionModules.Complaints] = "View",
                [PermissionModules.Settings] = "None"
            });
        }
        return db;
    }

    private static User AddUser(SmmsDbContext db, string username, string? occupancy = null,
        string status = "Active", string role = "Member")
    {
        var user = new User { Username = username, PasswordHash = "x", Role = role, Status = status, OccupancyType = occupancy };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static UserGroup AddGroup(SmmsDbContext db, string name, Dictionary<string, string> permissions)
    {
        var group = new UserGroup { Name = name, IsActive = true, Permissions = PermissionHelper.Serialize(permissions) };
        db.UserGroups.Add(group);
        db.SaveChanges();
        return group;
    }

    private static void Join(SmmsDbContext db, UserGroup g, User u)
    {
        db.UserGroupMembers.Add(new UserGroupMember { UserGroupId = g.Id, UserId = u.Id, AddedOn = DateTime.UtcNow });
        db.SaveChanges();
    }

    public void Dispose()
    {
        foreach (var c in _connections) c.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Occupancy decides the baseline ────────────────────────────────────────────────────

    /// <summary>A tenant rents the flat. They pay maintenance and raise complaints; the society's
    /// accounts are not theirs to read.</summary>
    [Fact]
    public async Task Tenant_CannotSeeTheSocietysMoney()
    {
        using var db = NewSociety();
        var user = AddUser(db, "tenant202", UserGroups.Tenant);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal(UserGroups.Tenant, access!.OccupancyGroup);
        foreach (var m in new[] { PermissionModules.Collections, PermissionModules.Expenses, PermissionModules.Income,
                                  PermissionModules.Liabilities, PermissionModules.Budgets, PermissionModules.Inventory,
                                  PermissionModules.Members, PermissionModules.Settings })
            Assert.Equal("None", access.Permissions[m]);

        Assert.Equal("View", access.Permissions[PermissionModules.Complaints]);
    }

    /// <summary>An owner is entitled to see how the society's money is collected and spent, even
    /// when they hold no committee position.</summary>
    [Fact]
    public async Task Owner_SeesTheAccountsButCannotChangeThem()
    {
        using var db = NewSociety();
        var user = AddUser(db, "owner501", UserGroups.Owner);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal(UserGroups.Owner, access!.OccupancyGroup);
        foreach (var m in new[] { PermissionModules.Collections, PermissionModules.Expenses, PermissionModules.Income,
                                  PermissionModules.Liabilities, PermissionModules.Budgets, PermissionModules.Inventory })
            Assert.Equal("View", access.Permissions[m]);

        Assert.Equal("None", access.Permissions[PermissionModules.Settings]);
        Assert.DoesNotContain(access.Permissions.Values, v => v == "Edit");
    }

    /// <summary>Occupancy unset means nobody has confirmed who lives there, so the cautious answer
    /// applies and the flat's accounts stay private until an admin says otherwise.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("something else")]
    public async Task UnsetOrUnrecognisedOccupancy_IsTreatedAsTenant(string? occupancy)
    {
        using var db = NewSociety();
        var user = AddUser(db, "unknown", occupancy);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal(UserGroups.Tenant, access!.OccupancyGroup);
        Assert.Equal("None", access.Permissions[PermissionModules.Expenses]);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("OWNER")]
    [InlineData(" Owner ")]
    public async Task OccupancyMatchIsCaseAndWhitespaceInsensitive(string occupancy)
    {
        using var db = NewSociety();
        var user = AddUser(db, "u", occupancy);
        Assert.Equal(UserGroups.Owner, (await new EffectivePermissionService(db).ResolveAsync(user.Id))!.OccupancyGroup);
    }

    /// <summary>Changing the field is the whole mechanism — there is no membership row to update,
    /// so it can never disagree with the occupancy an admin recorded.</summary>
    [Fact]
    public async Task ChangingOccupancy_ChangesAccessImmediately()
    {
        using var db = NewSociety();
        var resolver = new EffectivePermissionService(db);
        var user = AddUser(db, "flat502", UserGroups.Owner);

        Assert.Equal("View", (await resolver.ResolveAsync(user.Id))!.Permissions[PermissionModules.Expenses]);

        db.Users.Find(user.Id)!.OccupancyType = UserGroups.Tenant;
        db.SaveChanges();

        Assert.Equal("None", (await resolver.ResolveAsync(user.Id))!.Permissions[PermissionModules.Expenses]);
        Assert.Equal(0, await db.UserGroupMembers.CountAsync());
    }

    // ── Positions stack on top of occupancy ───────────────────────────────────────────────

    [Fact]
    public async Task OwnerWhoIsAlsoTreasurer_GetsEditOnMoney()
    {
        using var db = NewSociety();
        var user = AddUser(db, "rajesh", UserGroups.Owner);
        var treasurer = AddGroup(db, "Treasurer", new()
        {
            [PermissionModules.Expenses] = "Edit",
            [PermissionModules.Liabilities] = "Edit"
        });
        Join(db, treasurer, user);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal("Edit", access!.Permissions[PermissionModules.Expenses]);
        Assert.Equal("Edit", access.Permissions[PermissionModules.Liabilities]);
        Assert.Equal("View", access.Permissions[PermissionModules.Collections]);
        Assert.Contains("Treasurer", access.Groups);
    }

    /// <summary>A tenant elected onto the committee needs what the position grants, and the Tenant
    /// baseline must not veto it. Most permissive wins.</summary>
    [Fact]
    public async Task TenantWhoIsAlsoTreasurer_GetsThePositionsAccess()
    {
        using var db = NewSociety();
        var user = AddUser(db, "suresh", UserGroups.Tenant);
        var treasurer = AddGroup(db, "Treasurer", new() { [PermissionModules.Expenses] = "Edit" });
        Join(db, treasurer, user);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal("Edit", access!.Permissions[PermissionModules.Expenses]);
        Assert.Equal(UserGroups.Tenant, access.OccupancyGroup);
    }

    /// <summary>Serialize() fills anything unspecified with "View", so a group that is meant to be
    /// narrow has to say None explicitly. Pinned because otherwise a "Festival Committee" created
    /// with one permission ticked would quietly hand out the society's accounts as well.</summary>
    [Fact]
    public async Task NarrowGroup_GrantsOnlyWhatItStates()
    {
        using var db = NewSociety();
        var user = AddUser(db, "volunteer", UserGroups.Tenant);

        var everythingElseDenied = PermissionModules.All.ToDictionary(m => m, _ => "None");
        everythingElseDenied[PermissionModules.Complaints] = "Edit";
        var festival = AddGroup(db, "Festival Committee", everythingElseDenied);
        Join(db, festival, user);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal("Edit", access!.Permissions[PermissionModules.Complaints]);
        Assert.Equal("None", access.Permissions[PermissionModules.Expenses]);
        Assert.Equal("None", access.Permissions[PermissionModules.Collections]);
        Assert.Equal("None", access.Permissions[PermissionModules.Members]);
    }

    [Fact]
    public async Task RemovingThePosition_LeavesOnlyTheOccupancyBaseline()
    {
        using var db = NewSociety();
        var resolver = new EffectivePermissionService(db);
        var user = AddUser(db, "u", UserGroups.Tenant);
        var treasurer = AddGroup(db, "Treasurer", new() { [PermissionModules.Expenses] = "Edit" });
        Join(db, treasurer, user);

        Assert.Equal("Edit", (await resolver.ResolveAsync(user.Id))!.Permissions[PermissionModules.Expenses]);

        db.UserGroupMembers.Remove(db.UserGroupMembers.Single(m => m.UserId == user.Id));
        db.SaveChanges();

        Assert.Equal("None", (await resolver.ResolveAsync(user.Id))!.Permissions[PermissionModules.Expenses]);
    }

    [Fact]
    public async Task EditingAGroup_AffectsEveryHolderAtOnce()
    {
        using var db = NewSociety();
        var resolver = new EffectivePermissionService(db);
        var group = AddGroup(db, "Treasurer", new() { [PermissionModules.Expenses] = "Edit" });
        var owner = AddUser(db, "a", UserGroups.Owner);
        var tenant = AddUser(db, "c", UserGroups.Tenant);
        Join(db, group, owner);
        Join(db, group, tenant);

        Assert.Equal("Edit", (await resolver.ResolveAsync(owner.Id))!.Permissions[PermissionModules.Expenses]);
        Assert.Equal("Edit", (await resolver.ResolveAsync(tenant.Id))!.Permissions[PermissionModules.Expenses]);

        group.Permissions = PermissionHelper.Serialize(new() { [PermissionModules.Expenses] = "None" });
        db.SaveChanges();

        // The owner keeps View from their baseline; the tenant drops to None.
        Assert.Equal("View", (await resolver.ResolveAsync(owner.Id))!.Permissions[PermissionModules.Expenses]);
        Assert.Equal("None", (await resolver.ResolveAsync(tenant.Id))!.Permissions[PermissionModules.Expenses]);
    }

    [Fact]
    public async Task InactiveGroup_GrantsNothing()
    {
        using var db = NewSociety();
        var user = AddUser(db, "u", UserGroups.Tenant);
        var group = AddGroup(db, "Retired", new() { [PermissionModules.Expenses] = "Edit" });
        Join(db, group, user);
        group.IsActive = false;
        db.SaveChanges();

        Assert.Equal("None", (await new EffectivePermissionService(db).ResolveAsync(user.Id))!.Permissions[PermissionModules.Expenses]);
    }

    /// <summary>If the baselines have not been seeded yet the resolver must not lock anyone out;
    /// it falls back to the platform's read-only default.</summary>
    [Fact]
    public async Task WithNoOccupancyGroupsSeeded_FallsBackToView()
    {
        using var db = NewSociety(seedOccupancyGroups: false);
        var user = AddUser(db, "u", UserGroups.Tenant);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.All(PermissionModules.All, m => Assert.Equal("View", access!.Permissions[m]));
    }

    // ── Account state ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Pending")]
    public async Task NonActiveAccount_ResolvesToNothing(string status)
    {
        using var db = NewSociety();
        var user = AddUser(db, "gone", UserGroups.Owner, status);
        Assert.Null(await new EffectivePermissionService(db).ResolveAsync(user.Id));
    }

    [Fact]
    public async Task DeletedAccount_ResolvesToNothing()
    {
        using var db = NewSociety();
        Assert.Null(await new EffectivePermissionService(db).ResolveAsync(4242));
    }

    [Fact]
    public async Task Admin_ResolvesAsAdminWhateverTheirOccupancy()
    {
        using var db = NewSociety();
        var admin = AddUser(db, "admin", null, role: Roles.Admin);
        Assert.Equal(Roles.Admin, (await new EffectivePermissionService(db).ResolveAsync(admin.Id))!.Role);
    }

    // ── Combine() in isolation ────────────────────────────────────────────────────────────

    [Fact]
    public void Combine_WithNoGroups_IsTheViewDefault()
    {
        var combined = PermissionHelper.Combine([]);
        Assert.All(PermissionModules.All, m => Assert.Equal("View", combined[m]));
    }

    [Fact]
    public void Combine_CoversEveryModuleIncludingNewOnes()
    {
        var combined = PermissionHelper.Combine([PermissionHelper.Serialize(new() { [PermissionModules.Expenses] = "Edit" })]);
        Assert.Equal(PermissionModules.All.Length, combined.Count);
        Assert.Contains(PermissionModules.Inventory, combined.Keys);
    }

    [Fact]
    public void Combine_MostPermissiveWins()
    {
        var combined = PermissionHelper.Combine([
            PermissionHelper.Serialize(new() { [PermissionModules.Expenses] = "None" }),
            PermissionHelper.Serialize(new() { [PermissionModules.Expenses] = "Edit" })
        ]);
        Assert.Equal("Edit", combined[PermissionModules.Expenses]);
    }

    [Fact]
    public void Combine_ModuleDeniedByEveryGroup_IsNone()
    {
        var combined = PermissionHelper.Combine([
            PermissionHelper.Serialize(new() { [PermissionModules.Settings] = "None" }),
            PermissionHelper.Serialize(new() { [PermissionModules.Settings] = "None" })
        ]);
        Assert.Equal("None", combined[PermissionModules.Settings]);
    }

    // ── Controller queries must survive translation to SQL ────────────────────────────────

    /// <summary>The group detail screen reads members with a name-or-username sort. Doing that in
    /// the query threw at runtime because the fallback cannot be translated.</summary>
    [Fact]
    public async Task GroupMembers_AreReadableAndSortedByDisplayName()
    {
        using var db = NewSociety();
        var group = AddGroup(db, "Treasurer", new() { [PermissionModules.Expenses] = "Edit" });
        var withName = AddUser(db, "zzz-username", UserGroups.Owner);
        withName.Name = "Aaron";
        var noName = AddUser(db, "bbb-username", UserGroups.Owner);
        db.SaveChanges();
        Join(db, group, withName);
        Join(db, group, noName);

        var members = await db.UserGroupMembers.AsNoTracking()
            .Where(m => m.UserGroupId == group.Id)
            .Select(m => new { m.UserId, m.User!.Username, m.User.Name })
            .ToListAsync();
        var ordered = members.OrderBy(m => m.Name ?? m.Username, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(2, ordered.Count);
        Assert.Equal("Aaron", ordered[0].Name);
        Assert.Equal("bbb-username", ordered[1].Username);
    }
}
