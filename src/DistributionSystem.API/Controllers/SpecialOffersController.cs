using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Config;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.API.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class SpecialOffersController : ControllerBase
{
    private const string SpecialOfferType = "SpecialOffer";
    private readonly IUnitOfWork _unitOfWork;

    public SpecialOffersController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    [HttpGet("offers")]
    [AllowAnonymous]
    public async Task<IActionResult> GetActiveOffers(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var offers = await _unitOfWork.Repository<Promotion>().Query()
            .Where(p => p.PromotionType == SpecialOfferType && p.IsActive && p.EndDate >= now)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return Ok(ApiResponse<List<SpecialOfferDto>>.SuccessResponse(offers.Select(Map).ToList()));
    }

    [HttpGet("admin/offers")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminGetOffers(CancellationToken ct)
    {
        var offers = await _unitOfWork.Repository<Promotion>().Query()
            .Where(p => p.PromotionType == SpecialOfferType)
            .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
            .ToListAsync(ct);

        return Ok(ApiResponse<List<SpecialOfferDto>>.SuccessResponse(offers.Select(Map).ToList()));
    }

    [HttpPost("admin/offers")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminCreateOffer([FromBody] CreateSpecialOfferRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProductName) || string.IsNullOrWhiteSpace(request.OfferBrief))
            return BadRequest(ApiResponse<string>.ErrorResponse("Product name and offer brief are required"));

        var offer = new Promotion
        {
            Name = request.ProductName.Trim(),
            Description = request.OfferBrief.Trim(),
            PromotionType = SpecialOfferType,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddYears(10),
            IsActive = request.IsActive,
        };

        await _unitOfWork.Repository<Promotion>().AddAsync(offer, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Ok(ApiResponse<SpecialOfferDto>.SuccessResponse(Map(offer), "Offer created"));
    }

    [HttpPut("admin/offers/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminUpdateOffer(Guid id, [FromBody] UpdateSpecialOfferRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProductName) || string.IsNullOrWhiteSpace(request.OfferBrief))
            return BadRequest(ApiResponse<string>.ErrorResponse("Product name and offer brief are required"));

        var offer = await _unitOfWork.Repository<Promotion>().Query()
            .FirstOrDefaultAsync(p => p.Id == id && p.PromotionType == SpecialOfferType, ct);

        if (offer == null)
            return NotFound(ApiResponse<string>.ErrorResponse("Offer not found"));

        offer.Name = request.ProductName.Trim();
        offer.Description = request.OfferBrief.Trim();
        offer.IsActive = request.IsActive;
        offer.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Repository<Promotion>().Update(offer);
        await _unitOfWork.SaveChangesAsync(ct);

        return Ok(ApiResponse<SpecialOfferDto>.SuccessResponse(Map(offer), "Offer updated"));
    }

    [HttpDelete("admin/offers/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AdminDeleteOffer(Guid id, CancellationToken ct)
    {
        var offer = await _unitOfWork.Repository<Promotion>().Query()
            .FirstOrDefaultAsync(p => p.Id == id && p.PromotionType == SpecialOfferType, ct);

        if (offer == null)
            return NotFound(ApiResponse<string>.ErrorResponse("Offer not found"));

        _unitOfWork.Repository<Promotion>().Remove(offer);
        await _unitOfWork.SaveChangesAsync(ct);

        return Ok(ApiResponse<string>.SuccessResponse("Offer deleted"));
    }

    private static SpecialOfferDto Map(Promotion promotion) => new()
    {
        Id = promotion.Id,
        ProductName = promotion.Name,
        OfferBrief = promotion.Description ?? string.Empty,
        IsActive = promotion.IsActive,
        CreatedAt = promotion.CreatedAt
    };
}
