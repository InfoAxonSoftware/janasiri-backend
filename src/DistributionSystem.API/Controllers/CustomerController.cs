using System.Security.Claims;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Customer;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.DTOs.Region;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Customer management endpoints
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class CustomerController : ControllerBase
{
    private readonly ICustomerService _customerService;
    private readonly INotificationService _notificationService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICoordinatorService _coordinatorService;
    private readonly ICustomerRegistrationService _registrationService;
    private readonly IRegionService _regionService;
    private readonly IFileStorageService _fileStorage;

    public CustomerController(ICustomerService customerService, INotificationService notificationService, IUnitOfWork unitOfWork, ICoordinatorService coordinatorService, ICustomerRegistrationService registrationService, IRegionService regionService, IFileStorageService fileStorage)
    {
        _customerService = customerService;
        _notificationService = notificationService;
        _unitOfWork = unitOfWork;
        _coordinatorService = coordinatorService;
        _registrationService = registrationService;
        _regionService = regionService;
        _fileStorage = fileStorage;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<Guid> ResolveCustomerProfileIdAsync(Guid customerIdOrUserId, CancellationToken ct)
    {
        // Primary expected input is CustomerProfile.Id.
        try
        {
            var byProfileId = await _customerService.GetByIdAsync(customerIdOrUserId, ct);
            return byProfileId.Id;
        }
        catch
        {
            // Fallback: some callers may pass User.Id instead.
        }

        var byUserId = await _customerService.GetByUserIdAsync(customerIdOrUserId, ct);
        return byUserId.Id;
    }

    private async Task<bool> IsCustomerVisibleToRepToday(Guid repUserId, Guid customerId, CancellationToken ct)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct);
        if (rep == null) return false;

        return await _unitOfWork.Repository<RouteCustomer>().Query()
            .Include(rc => rc.Route)
            .AnyAsync(rc =>
                rc.CustomerId == customerId &&
                rc.Route.IsActive &&
                rc.Route.AssignedReps.Any(rr => rr.RepId == rep.Id), ct);
    }

    // ===== ADMIN ENDPOINTS =====

    /// <summary>Get all customers with pagination (Admin)</summary>
    [HttpGet("admin/customers")]
    [Authorize(Roles = "Admin,SuperAdmin,SalesCoordinator")]
    public async Task<IActionResult> GetAllCustomers([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] string? sortBy = null, [FromQuery] string? sortOrder = "asc",
        [FromQuery] bool? isActive = null, [FromQuery] Guid? assignedRepId = null, [FromQuery] Guid? assignedCoordinatorId = null,
        [FromQuery] Guid? regionId = null, [FromQuery] Guid? subRegionId = null,
        [FromQuery] DateTime? createdFrom = null, [FromQuery] DateTime? createdTo = null, CancellationToken ct = default)
    {
        if (User.IsInRole("SalesCoordinator"))
        {
            var coordinator = await _coordinatorService.GetMyProfileAsync(GetUserId(), ct);
            assignedCoordinatorId = coordinator.Id;
        }

        var result = await _customerService.GetAllAsync(page, pageSize, search, sortBy, sortOrder, isActive, assignedRepId, assignedCoordinatorId, regionId, subRegionId, null, null, null, createdFrom, createdTo, ct);
        return Ok(ApiResponse<PagedResult<CustomerDto>>.SuccessResponse(result));
    }

    /// <summary>Get customer by ID (Admin)</summary>
    [HttpGet("admin/customers/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin,SalesCoordinator")]
    public async Task<IActionResult> GetCustomer(Guid id, CancellationToken ct)
    {
        var result = await _customerService.GetByIdAsync(id, ct);

        if (User.IsInRole("SalesCoordinator"))
        {
            var coordinator = await _coordinatorService.GetMyProfileAsync(GetUserId(), ct);
            if (result.AssignedCoordinatorId != coordinator.Id)
                return Forbid();
        }

        return Ok(ApiResponse<CustomerDto>.SuccessResponse(result));
    }

    /// <summary>Create a new customer (Admin)</summary>
    [HttpPost("admin/customers")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        // Admin-created customers are immediately Approved — no rep approval flow
        request.IsAdminCreated = true;
        var result = await _customerService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetCustomer), new { id = result.Id }, ApiResponse<CustomerDto>.SuccessResponse(result, "Customer created"));
    }

    /// <summary>Soft-delete a customer — moves to trash (Admin)</summary>
    [HttpDelete("admin/customers/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> SoftDeleteCustomer(Guid id, CancellationToken ct)
    {
        var deletedBy = User.FindFirstValue(ClaimTypes.Name) ?? GetUserId().ToString();
        await _customerService.SoftDeleteAsync(id, deletedBy, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Customer moved to trash"));
    }

    /// <summary>Get trashed (soft-deleted) customers (Admin)</summary>
    [HttpGet("admin/customers/trash")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetTrashedCustomers([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _customerService.GetTrashedAsync(page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<CustomerDto>>.SuccessResponse(result));
    }

    /// <summary>Restore a customer from trash (Admin)</summary>
    [HttpPut("admin/customers/{id}/restore")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> RestoreCustomer(Guid id, CancellationToken ct)
    {
        await _customerService.RestoreAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Customer restored"));
    }

    /// <summary>Update a customer (Admin)</summary>
    [HttpPut("admin/customers/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateCustomer(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        var result = await _customerService.UpdateAsync(id, request, ct);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(result, "Customer updated"));
    }

    /// <summary>Update full customer registration details and documents (Admin)</summary>
    [HttpPut("admin/customers/{id}/registration-details")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateCustomerRegistrationDetails(
        Guid id,
        [FromForm] UpdateCustomerRegistrationDetailsRequest request,
        IFormFile? businessRegDoc,
        IFormFile? businessAddressDoc,
        IFormFile? vatDoc,
        CancellationToken ct)
    {
        var baseUrl = "";
        var result = await _customerService.UpdateRegistrationDetailsAsync(
            id,
            request,
            businessRegDoc,
            businessAddressDoc,
            vatDoc,
            baseUrl,
            ct);

        return Ok(ApiResponse<CustomerSummaryDto>.SuccessResponse(result, "Customer details updated"));
    }

    /// <summary>Get / manage special pricing for a customer (Admin)</summary>
    [HttpGet("admin/customers/{customerId}/special-prices")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetSpecialPrices(Guid customerId, CancellationToken ct)
    {
        var resolvedCustomerId = await ResolveCustomerProfileIdAsync(customerId, ct);
        var result = await _customerService.GetSpecialPricesAsync(resolvedCustomerId, ct);
        return Ok(ApiResponse<List<PriceDetailDto>>.SuccessResponse(result));
    }

    /// <summary>Save special prices for a customer (Admin)</summary>
    [HttpPost("admin/customers/{customerId}/special-prices")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> SaveSpecialPrices(Guid customerId, [FromBody] List<SpecialPriceUpdateRequest> request, CancellationToken ct)
    {
        var resolvedCustomerId = await ResolveCustomerProfileIdAsync(customerId, ct);
        await _customerService.SaveSpecialPricesAsync(resolvedCustomerId, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Special prices updated"));
    }

    /// <summary>Get special prices for a customer (Coordinator)</summary>
    [HttpGet("coordinator/customers/{customerId}/special-prices")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> GetSpecialPricesCoordinator(Guid customerId, CancellationToken ct)
    {
        var result = await _customerService.GetSpecialPricesAsync(customerId, ct);
        return Ok(ApiResponse<List<PriceDetailDto>>.SuccessResponse(result));
    }

    /// <summary>Save special prices for a customer (Coordinator)</summary>
    [HttpPost("coordinator/customers/{customerId}/special-prices")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> SaveSpecialPricesCoordinator(Guid customerId, [FromBody] List<SpecialPriceUpdateRequest> request, CancellationToken ct)
    {
        await _customerService.SaveSpecialPricesAsync(customerId, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Special prices updated"));
    }

    /// <summary>Get customer summary with statistics (Admin)</summary>
    [HttpGet("admin/customers/{id}/summary")]
    [Authorize(Roles = "Admin,SuperAdmin,SalesCoordinator")]
    public async Task<IActionResult> GetCustomerSummary(Guid id, CancellationToken ct)
    {
        if (User.IsInRole("SalesCoordinator"))
        {
            var coordinator = await _coordinatorService.GetMyProfileAsync(GetUserId(), ct);
            var customer = await _customerService.GetByIdAsync(id, ct);
            if (customer.AssignedCoordinatorId != coordinator.Id)
                return Forbid();
        }

        var baseUrl = "";
        var result = await _customerService.GetSummaryAsync(id, baseUrl, ct);
        return Ok(ApiResponse<CustomerSummaryDto>.SuccessResponse(result));
    }

    /// <summary>Activate/Deactivate customer (Admin)</summary>
    [HttpPut("admin/customers/{id}/status")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ToggleCustomerStatus(Guid id, [FromBody] bool isActive, CancellationToken ct)
    {
        if (isActive)
            await _customerService.ActivateAsync(id, ct);
        else
            await _customerService.DeactivateAsync(id, ct);
        return Ok(ApiResponse<string>.SuccessResponse($"Customer {(isActive ? "activated" : "deactivated")}"));
    }

    // ===== REP ENDPOINTS =====

    /// <summary>Get assigned customers (Rep)</summary>
    [HttpGet("rep/customers")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetCustomers([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var result = await _customerService.GetByRepAsync(userId, page, pageSize, search, ct);
        return Ok(ApiResponse<PagedResult<CustomerDto>>.SuccessResponse(result));
    }

    /// <summary>Get assigned customer details (Rep)</summary>
    [HttpGet("rep/customers/{id}")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetCustomer(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (!await IsCustomerVisibleToRepToday(userId, id, ct))
            return Forbid();

        var result = await _customerService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(result));
    }

    /// <summary>Get assigned customer summary (Rep)</summary>
    [HttpGet("rep/customers/{id}/summary")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepGetCustomerSummary(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (!await IsCustomerVisibleToRepToday(userId, id, ct))
            return Forbid();

        var baseUrl = "";
        var result = await _customerService.GetSummaryAsync(id, baseUrl, ct);
        return Ok(ApiResponse<CustomerSummaryDto>.SuccessResponse(result));
    }

    /// <summary>Register a new customer and assign to the logged-in rep</summary>
    [HttpPost("rep/customers")]
    [Authorize(Roles = "SalesRep")]
    public async Task<IActionResult> RepCreateCustomer([FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var repProfile = await _unitOfWork.Repository<SalesRepProfile>().Query().FirstOrDefaultAsync(r => r.UserId == userId, ct);
        if (repProfile == null)
            return BadRequest(ApiResponse<string>.ErrorResponse("Sales rep profile not found"));

        // ensure customer is assigned to this rep
        request.AssignedRepId = repProfile.Id;
        var result = await _customerService.CreateAsync(request, ct);

        // Notify coordinators about new customer registration
        await _notificationService.SendToRoleAsync(new BroadcastNotificationRequest
        {
            Role = "SalesCoordinator",
            Title = "New Customer Registered",
            Message = $"New customer '{result.ShopName}' registered by rep {repProfile.FullName ?? repProfile.EmployeeCode}",
            Type = "CustomerRegistration"
        }, ct);

        return CreatedAtAction(nameof(RepGetCustomer), new { id = result.Id }, ApiResponse<CustomerDto>.SuccessResponse(result, "Customer created"));
    }

    // ===== CUSTOMER ENDPOINTS =====

    /// <summary>Get own profile (Customer)</summary>
    [HttpGet("customer/profile")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _customerService.GetByUserIdAsync(userId, ct);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(result));
    }

    /// <summary>Update own profile (Customer)</summary>
    [HttpPut("customer/profile")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _customerService.UpdateByUserIdAsync(userId, request, ct);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(result, "Profile updated"));
    }
    /// <summary>Get customer filter options (Admin)</summary>
    [HttpGet("admin/customers/filters")]
    [Authorize(Roles = "Admin,SuperAdmin,SalesCoordinator")]
    public async Task<IActionResult> GetCustomerFilterOptions(CancellationToken ct = default)
    {
        var result = await _customerService.GetFilterOptionsAsync(ct);
        return Ok(ApiResponse<CustomerFilterOptionsDto>.SuccessResponse(result));
    }
    /// <summary>Get own ledger / account summary (Customer)</summary>
    [HttpGet("customer/ledger")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> GetMyLedger(CancellationToken ct)
    {
        var userId = GetUserId();
        var customer = await _customerService.GetByUserIdAsync(userId, ct);
        var baseUrl = "";
        var result = await _customerService.GetSummaryAsync(customer.Id, baseUrl, ct);
        return Ok(ApiResponse<CustomerSummaryDto>.SuccessResponse(result));
    }

    // ===== CUSTOMER REGISTRATION REQUEST ENDPOINTS =====

    /// <summary>List regions for public customer registration form</summary>
    [HttpGet("customer-registrations/regions")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRegistrationRegions(CancellationToken ct)
    {
        var result = await _regionService.GetAllRegionsAsync(ct);
        return Ok(ApiResponse<List<RegionDto>>.SuccessResponse(result));
    }

    /// <summary>List sub-regions for public customer registration form</summary>
    [HttpGet("customer-registrations/regions/{regionId:guid}/sub-regions")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRegistrationSubRegions(Guid regionId, CancellationToken ct)
    {
        var result = await _regionService.GetSubRegionsByRegionAsync(regionId, ct);
        return Ok(ApiResponse<List<SubRegionDto>>.SuccessResponse(result));
    }

    /// <summary>Submit a new customer registration request (Public / Anonymous)</summary>
    [HttpPost("customer-registrations")]
    [AllowAnonymous]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitRegistration(
        [FromForm] SubmitRegistrationFormRequest data,
        IFormFile? businessRegDoc,
        IFormFile? businessAddressDoc,
        IFormFile? vatDoc,
        CancellationToken ct)
    {
        var result = await _registrationService.SubmitAsync(data, businessRegDoc, businessAddressDoc, vatDoc, ct);
        
        // Notify all admins of new registration
        await _notificationService.SendToRoleAsync(new BroadcastNotificationRequest
        {
            Role = "Admin",
            Title = "New Customer Registration",
            Message = $"New registration request from '{result.CustomerName}' requires review.",
            Type = "CustomerRegistration"
        }, ct);

        // Also notify active coordinators in the selected region (if provided)
        if (data.RegionId.HasValue)
        {
            var coordinatorUserIds = await _unitOfWork.Repository<CoordinatorProfile>().Query()
                .Where(c => c.RegionId == data.RegionId.Value && c.User.IsActive)
                .Select(c => c.UserId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var coordinatorUserId in coordinatorUserIds)
            {
                await _notificationService.SendToUserAsync(new SendNotificationRequest
                {
                    UserId = coordinatorUserId,
                    Title = "New Customer Registration",
                    Message = $"New registration request from '{result.CustomerName}' in your region requires review.",
                    Type = "CustomerRegistration"
                }, ct);
            }
        }
        
        return StatusCode(201, ApiResponse<CustomerRegistrationRequestDto>.SuccessResponse(result, "Registration submitted successfully. Our team will review your application."));
    }

    /// <summary>List all customer registration requests (Admin)</summary>
    [HttpGet("admin/customer-registrations")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetRegistrationRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var baseUrl = "";
        var result = await _registrationService.GetAllAsync(page, pageSize, status, baseUrl, ct);
        return Ok(ApiResponse<PagedResult<CustomerRegistrationRequestDto>>.SuccessResponse(result));
    }

    /// <summary>Get a single registration request by ID (Admin)</summary>
    [HttpGet("admin/customer-registrations/{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetRegistrationRequest(Guid id, CancellationToken ct)
    {
        var baseUrl = "";
        var result = await _registrationService.GetByIdAsync(id, baseUrl, ct);
        return Ok(ApiResponse<CustomerRegistrationRequestDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Download a customer registration KYC document (business-reg / business-address / vat).
    /// Admin/SuperAdmin can access any request; SalesCoordinator only requests within their
    /// assigned region. Access is resolved through the owning registration record, not by
    /// trusting a bare file path/GUID.
    /// </summary>
    [HttpGet("customer-registrations/{id:guid}/documents/{docType}")]
    [Authorize(Roles = "Admin,SuperAdmin,SalesCoordinator")]
    public async Task<IActionResult> DownloadRegistrationDocument(Guid id, string docType, CancellationToken ct)
    {
        if (User.IsInRole("SalesCoordinator") && !User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
            await _registrationService.GetByIdForCoordinatorAsync(id, GetUserId(), "", ct); // throws NotFoundException if out of scope
        else
            await _registrationService.GetByIdAsync(id, "", ct); // throws NotFoundException if the request doesn't exist

        var reference = await _registrationService.GetDocumentReferenceAsync(id, docType, ct);
        if (reference is null) return NotFound();

        var file = await _fileStorage.ReadAsync(reference.Value.StorageKey, ct);
        if (file is null) return NotFound();

        Response.Headers.CacheControl = "no-store, private";
        return File(file.Content, file.ContentType, reference.Value.DownloadName + Path.GetExtension(file.FileName));
    }

    [HttpPut("admin/customer-registrations/{id}/review")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ReviewRegistration(Guid id, [FromBody] ReviewRegistrationRequest request, CancellationToken ct)
    {
        var adminId = GetUserId();
        var result = await _registrationService.ReviewAsync(id, request, adminId, ct);

        if (result.Status == "Approved" && result.AssignedCoordinatorId.HasValue)
        {
            // Get the coordinator's UserId to send targeted notification
            var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
                .FirstOrDefaultAsync(c => c.Id == result.AssignedCoordinatorId.Value, ct);
            
            if (coordinator != null)
            {
                await _notificationService.SendToUserAsync(new SendNotificationRequest
                {
                    UserId = coordinator.UserId,
                    Title = "New Customer Assigned",
                    Message = $"Customer '{result.CustomerName}' has been assigned to you.",
                    Type = "CustomerAssignment"
                }, ct);
            }
        }

        return Ok(ApiResponse<CustomerRegistrationRequestDto>.SuccessResponse(result, $"Registration {result.Status.ToLower()}"));
    }

    /// <summary>Get coordinator options for assigning to registration requests (Admin)</summary>
    [HttpGet("admin/customer-registrations/coordinators")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetCoordinatorOptions(CancellationToken ct)
    {
        var result = await _registrationService.GetCoordinatorOptionsAsync(ct);
        return Ok(ApiResponse<List<CoordinatorOptionDto>>.SuccessResponse(result));
    }

    /// <summary>List region-scoped customer registration requests for coordinator review</summary>
    [HttpGet("coordinator/customer-registrations")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorGetRegistrationRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = "Pending",
        CancellationToken ct = default)
    {
        var baseUrl = "";
        var result = await _registrationService.GetForCoordinatorAsync(GetUserId(), page, pageSize, status, baseUrl, ct);
        return Ok(ApiResponse<PagedResult<CustomerRegistrationRequestDto>>.SuccessResponse(result));
    }

    /// <summary>Approve or reject a registration request (Coordinator for own region)</summary>
    [HttpPut("coordinator/customer-registrations/{id}/review")]
    [Authorize(Roles = "SalesCoordinator")]
    public async Task<IActionResult> CoordinatorReviewRegistration(Guid id, [FromBody] ReviewRegistrationRequest request, CancellationToken ct)
    {
        var baseUrl = "";
        var result = await _registrationService.ReviewByCoordinatorAsync(id, request, GetUserId(), baseUrl, ct);
        return Ok(ApiResponse<CustomerRegistrationRequestDto>.SuccessResponse(result, $"Registration {result.Status.ToLower()}"));
    }

    /// <summary>Admin creates a customer directly – auto-approves, creates user account and sends credentials email.</summary>
    [HttpPost("admin/customer-registrations/admin-create")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> AdminCreateCustomer(
        [FromForm] AdminCreateRegistrationRequest data,
        IFormFile? businessRegDoc,
        IFormFile? businessAddressDoc,
        IFormFile? vatDoc,
        CancellationToken ct)
    {
        var adminId = GetUserId();
        var result = await _registrationService.AdminCreateAsync(data, businessRegDoc, businessAddressDoc, vatDoc, adminId, ct);

        // Notify assigned coordinator (if any)
        if (result.AssignedCoordinatorId.HasValue)
        {
            var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
                .FirstOrDefaultAsync(c => c.Id == result.AssignedCoordinatorId.Value, ct);
            if (coordinator != null)
            {
                await _notificationService.SendToUserAsync(new SendNotificationRequest
                {
                    UserId = coordinator.UserId,
                    Title = "New Customer Assigned",
                    Message = $"Customer '{result.CustomerName}' has been assigned to you.",
                    Type = "CustomerAssignment"
                }, ct);
            }
        }

        return StatusCode(201, ApiResponse<CustomerRegistrationRequestDto>.SuccessResponse(result, "Customer created successfully."));
    }
}


