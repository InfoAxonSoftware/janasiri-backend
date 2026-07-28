using System.Globalization;
using System.Text.Json;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.RepPayments;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.UnitTests;

public class RepPaymentTimestampTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PostAndGet_PreserveTheSameInstant_WhenLegacyNpgsqlReturnsServerLocalTime(
        int serverUtcOffsetHours)
    {
        using var fixture = new RepPaymentServiceFixture();
        var postDto = await fixture.CreateAndSettleAsync(
            fixture.RepAUserId,
            "Timestamp Test",
            1250m,
            expectedNotifications: 3);

        var persisted = await fixture.ReadDb.RepPayments
            .AsNoTracking()
            .SingleAsync(payment => payment.Id == postDto.Id);

        persisted.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        persisted.UpdatedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        persisted.CreatedAt.Should().Be(postDto.CreatedAt);
        persisted.UpdatedAt.Should().Be(postDto.UpdatedAt);

        var serverTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            $"UTC{serverUtcOffsetHours:+00;-00;+00}",
            TimeSpan.FromHours(serverUtcOffsetHours),
            "Simulated server time",
            "Simulated server time");

        var queriedEntity = new RepPayment
        {
            Id = persisted.Id,
            RepId = persisted.RepId,
            CustomerName = persisted.CustomerName,
            Amount = persisted.Amount,
            Status = persisted.Status,
            CreatedAt = AsLegacyLocal(persisted.CreatedAt, serverTimeZone),
            UpdatedAt = AsLegacyLocal(persisted.UpdatedAt!.Value, serverTimeZone),
        };

        queriedEntity.CreatedAt.Kind.Should().Be(DateTimeKind.Local);
        queriedEntity.UpdatedAt!.Value.Kind.Should().Be(DateTimeKind.Local);

        var getDto = RepPaymentService.MapToDto(
            queriedEntity,
            "Rep A",
            null,
            serverTimeZone);

        getDto.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        getDto.UpdatedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        getDto.CreatedAt.Should().Be(postDto.CreatedAt);
        getDto.UpdatedAt.Should().Be(postDto.UpdatedAt);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var postJson = JsonSerializer.Serialize(
            ApiResponse<RepPaymentDto>.SuccessResponse(postDto),
            jsonOptions);
        var getJson = JsonSerializer.Serialize(
            ApiResponse<PagedResult<RepPaymentDto>>.SuccessResponse(new PagedResult<RepPaymentDto>
            {
                Items = [getDto],
                TotalCount = 1,
                Page = 1,
                PageSize = 20,
            }),
            jsonOptions);

        var postTimestamp = ReadPostTimestamp(postJson);
        var getTimestamp = ReadGetTimestamp(getJson);

        postTimestamp.Should().EndWith("Z");
        getTimestamp.Should().EndWith("Z");
        ParseInstant(getTimestamp).Should().Be(ParseInstant(postTimestamp));
    }

    private static DateTime AsLegacyLocal(DateTime utcValue, TimeZoneInfo serverTimeZone)
    {
        var wallClock = TimeZoneInfo.ConvertTimeFromUtc(utcValue, serverTimeZone);
        return DateTime.SpecifyKind(wallClock, DateTimeKind.Local);
    }

    private static string ReadPostTimestamp(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").GetProperty("createdAt").GetString()!;
    }

    private static string ReadGetTimestamp(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").GetProperty("items")[0]
            .GetProperty("createdAt").GetString()!;
    }

    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
