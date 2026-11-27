using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using DistributionSystem.Application.DTOs.QuickRequests;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class QuickRequestService : IQuickRequestService
{
    private readonly IUnitOfWork _uow;
    private readonly IFileStorageService _fileStorage;
    private readonly INotificationPublisher? _publisher;
    private readonly ILogger<QuickRequestService>? _logger;

    // Unambiguous alphanumeric chars (no 0/O/1/I/L confusion)
    private static readonly char[] _numberChars =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private const long MaxImageSize = 5 * 1024 * 1024;
    private const long MaxPdfSize = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    private static readonly HashSet<string> AllowedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

    private static readonly HashSet<string> AllowedPdfTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf"
        };

    private static readonly HashSet<string> AllowedPdfExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf"
        };

    public QuickRequestService(IUnitOfWork uow, IFileStorageService fileStorage, INotificationPublisher? publisher = null, ILogger<QuickRequestService>? logger = null)
    {
        _uow = uow;
        _fileStorage = fileStorage;
        _publisher = publisher;
        _logger = logger;
    }

    /// <summary>
    /// Generates a random unique request number like QO-X4K9P2A7.
    /// Retries until the number doesn't already exist in the database.
    /// </summary>
    private async Task<string> GenerateUniqueNumberAsync(
        QuickRequestType type,
        CancellationToken ct)
    {
        var prefix = type switch
        {
            QuickRequestType.Order => "QO",
            QuickRequestType.Quotation => "QQ",
            _ => throw new BusinessException(
                "Invalid quick request type.",
                "INVALID_QUICK_REQUEST_TYPE"),
        };

        string number;

        do
        {
            var random = new string(
                Enumerable.Range(0, 8)
                    .Select(_ => _numberChars[
                        Random.Shared.Next(_numberChars.Length)])
                    .ToArray());

            number = $"{prefix}-{random}";
        }
        while (await _uow.Repository<QuickRequest>().Query()
            .AnyAsync(q => q.RequestNumber == number, ct));

        return number;
    }

    // ── Rep ──────────────────────────────────────────────────────────────────

    public async Task<QuickRequestDto> CreateAsync(
        Guid repUserId,
        CreateQuickRequestDto dto,
        CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        if (!Enum.TryParse<QuickRequestType>(
                dto.Type,
                ignoreCase: true,
                out var type))
        {
            throw new BusinessException(
                "Select Order or Quotation.",
                "INVALID_QUICK_REQUEST_TYPE");
        }

        if (string.IsNullOrWhiteSpace(dto.CustomerName))
        {
            throw new BusinessException(
                "Customer name is required.",
                "QUICK_REQUEST_CUSTOMER_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(dto.Details))
        {
            throw new BusinessException(
                "Add at least one item.",
                "QUICK_REQUEST_DETAILS_REQUIRED");
        }

        var number = await GenerateUniqueNumberAsync(type, ct);

        var request = new QuickRequest
        {
            RequestNumber = number,
            Type = type,
            CustomerName = dto.CustomerName.Trim(),
            Details = dto.Details.Trim(),
            RepId = rep.Id,
            CreatedBy = rep.FullName,
        };

        await _uow.Repository<QuickRequest>().AddAsync(request, ct);
        await _uow.SaveChangesAsync(ct);

        return MapToDto(request, rep.FullName);
    }

    public async Task<QuickRequestDto> AddImagesAsync(
        Guid requestId,
        Guid repUserId,
        IList<IFormFile> images,
        CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        // File upload must respect both global Admin deletion and this Rep's
        // personal Trash. A direct POST must not modify a hidden record.
        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var exists = await _uow.Repository<QuickRequest>().Query()
            .AsNoTracking()
            .AnyAsync(
                r => r.Id == requestId && r.RepId == rep.Id &&
                    !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                    !personalTrash.Any(t => t.UserId == repUserId &&
                        t.RecordKind == PersonalRecordTrash.QuickRequestKind &&
                        t.RecordId == r.Id),
                ct);

        if (!exists)
            throw new NotFoundException("Quick request", requestId);

        var attachmentEntities = new List<QuickRequestAttachment>();

        foreach (var file in images)
        {
            if (file.Length == 0)
                continue;

            ValidateAttachment(file);

            attachmentEntities.Add(
                await SaveAttachmentAsync(requestId, file, ct));
        }

        if (attachmentEntities.Count > 0)
        {
            await _uow.Repository<QuickRequestAttachment>()
                .AddRangeAsync(attachmentEntities, ct);

            await _uow.SaveChangesAsync(ct);
        }

        // Return the updated request
        var request = await _uow.Repository<QuickRequest>().Query()
            .AsNoTracking()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(
                r => r.Id == requestId,
                ct);

        if (request == null)
            throw new NotFoundException("Quick request", requestId);

        return MapToDto(
            request,
            request.Rep?.FullName ?? string.Empty);
    }

    public async Task<List<QuickRequestDto>> GetRepRequestsAsync(
        Guid repUserId,
        string? type = null,
        CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .Where(r =>
                r.RepId == rep.Id &&
                !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                !personalTrash.Any(t => t.UserId == repUserId &&
                    t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == r.Id));

        if (!string.IsNullOrEmpty(type) &&
            Enum.TryParse<QuickRequestType>(
                type,
                ignoreCase: true,
                out var t))
        {
            query = query.Where(r => r.Type == t);
        }

        var list = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return list
            .Select(r => MapToDto(
                r,
                r.Rep?.FullName ?? string.Empty))
            .ToList();
    }

    public async Task<QuickRequestDto> GetRepRequestByIdAsync(
        Guid requestId,
        Guid repUserId,
        CancellationToken ct = default,
        bool includePersonalTrash = false)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);

        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(
                r => r.Id == requestId && r.RepId == rep.Id &&
                    !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                    !personalTrash.Any(t => t.UserId == repUserId &&
                        t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == r.Id &&
                        (!includePersonalTrash || t.PurgedAt != null)),
                ct)
            ?? throw new NotFoundException(
                "Quick request",
                requestId);

        return MapToDto(
            request,
            request.Rep?.FullName ?? string.Empty);
    }

    // ── Public guest customer portal ─────────────────────────────────────────

    public async Task<GuestQuickOrderTrackingDto> TrackGuestOrderAsync(
        string requestNumber,
        string phoneNumber,
        CancellationToken ct = default)
    {
        var request = await GetVerifiedGuestOrderAsync(requestNumber, phoneNumber, ct);
        var contactPerson = ReadGuestDetail(request.Details, "Contact person");
        var contactNumber = ReadGuestDetail(request.Details, "Contact number");
        var attachments = request.Images
            .Select((image, index) => new GuestQuickOrderTrackingAttachmentDto
            {
                Id = image.Id,
                OriginalFileName = $"Photo {index + 1}",
                ContentType = "image/*",
                SizeBytes = 0,
                UploadedAt = image.UploadedAt,
            })
            .Concat(request.Attachments.Select(attachment => new GuestQuickOrderTrackingAttachmentDto
            {
                Id = attachment.Id,
                OriginalFileName = attachment.OriginalFileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes,
                UploadedAt = attachment.UploadedAt,
            }))
            .OrderBy(attachment => attachment.UploadedAt)
            .ToList();

        return new GuestQuickOrderTrackingDto
        {
            RequestNumber = request.RequestNumber,
            BusinessName = request.CustomerName,
            ContactPerson = contactPerson,
            ContactNumberMasked = MaskGuestPhone(contactNumber),
            Status = request.Status.ToString(),
            CreatedAt = AsUtc(request.CreatedAt),
            Items = ParseGuestOrderItems(request.Details),
            Attachments = attachments,
        };
    }

    public async Task<string?> GetGuestAttachmentStorageKeyAsync(
        string requestNumber,
        string phoneNumber,
        Guid attachmentId,
        CancellationToken ct = default)
    {
        var request = await GetVerifiedGuestOrderAsync(requestNumber, phoneNumber, ct);
        var attachment = request.Attachments.FirstOrDefault(file => file.Id == attachmentId);
        if (attachment != null)
            return attachment.StorageKey;

        var image = request.Images.FirstOrDefault(file => file.Id == attachmentId);
        return image?.ImageUrl;
    }

    private async Task<QuickRequest> GetVerifiedGuestOrderAsync(
        string requestNumber,
        string phoneNumber,
        CancellationToken ct)
    {
        var normalizedRequestNumber = (requestNumber ?? string.Empty).Trim().ToUpperInvariant();
        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .FirstOrDefaultAsync(r =>
                r.RequestNumber == normalizedRequestNumber &&
                r.Type == QuickRequestType.Order &&
                r.CreatedBy == "Quick Order Portal" &&
                !r.IsDeletedByAdmin &&
                !r.IsPurgedByAdmin,
                ct);

        var storedPhone = ReadGuestDetail(request?.Details ?? string.Empty, "Contact number");
        var suppliedDigits = NormalizeGuestPhone(phoneNumber);
        var storedDigits = NormalizeGuestPhone(storedPhone);
        if (request == null ||
            suppliedDigits.Length < 7 ||
            storedDigits.Length < 7 ||
            !string.Equals(suppliedDigits, storedDigits, StringComparison.Ordinal))
        {
            // Do not reveal whether a reference exists when the phone does not match.
            throw new NotFoundException("Guest order", normalizedRequestNumber);
        }

        return request;
    }

    private static string ReadGuestDetail(string details, string label)
    {
        var prefix = label + ":";
        var line = (details ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return line == null ? string.Empty : line[prefix.Length..].Trim();
    }

    private static string NormalizeGuestPhone(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.StartsWith("94", StringComparison.Ordinal) && digits.Length == 11)
            return "0" + digits[2..];
        return digits;
    }

    private static string MaskGuestPhone(string? value)
    {
        var digits = NormalizeGuestPhone(value);
        if (digits.Length <= 4)
            return new string('•', digits.Length);
        return new string('•', digits.Length - 4) + digits[^4..];
    }

    private static List<GuestQuickOrderItemDto> ParseGuestOrderItems(string details)
    {
        var result = new List<GuestQuickOrderItemDto>();
        var lines = (details ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var itemsStart = Array.FindIndex(lines, line =>
            string.Equals(line.Trim(), "ORDER ITEMS", StringComparison.OrdinalIgnoreCase));
        if (itemsStart < 0)
            return result;

        var pattern = new Regex(
            @"^\s*\d+\.\s*(?<description>.*?)\s*\|\s*Size:\s*(?<size>.*?)\s*\|\s*Brand:\s*(?<brand>.*?)\s*\|\s*Quantity:\s*(?<quantity>\d+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var line in lines.Skip(itemsStart + 1))
        {
            var match = pattern.Match(line);
            if (!match.Success || !int.TryParse(match.Groups["quantity"].Value, out var quantity))
                continue;

            var size = match.Groups["size"].Value.Trim();
            var brand = match.Groups["brand"].Value.Trim();
            result.Add(new GuestQuickOrderItemDto
            {
                Description = match.Groups["description"].Value.Trim(),
                Size = string.Equals(size, "Not specified", StringComparison.OrdinalIgnoreCase) ? null : size,
                Brand = string.Equals(brand, "Not specified", StringComparison.OrdinalIgnoreCase) ? null : brand,
                Quantity = quantity,
            });
        }

        return result;
    }
    public async Task<QuickRequestDto> CreateGuestAsync(
        CreateGuestQuickOrderDto dto,
        IList<IFormFile> attachments,
        CancellationToken ct = default)
    {
        static string Clean(string? value) =>
            string.Join(" ", (value ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Trim();

        var businessName = Clean(dto.BusinessName);
        var contactPerson = Clean(dto.ContactPerson);
        var phoneNumber = Clean(dto.PhoneNumber);

        if (businessName.Length is < 2 or > 200)
            throw new BusinessException("Enter a valid business name (2–200 characters).", "GUEST_ORDER_BUSINESS_REQUIRED");

        if (contactPerson.Length is < 2 or > 120)
            throw new BusinessException("Enter a valid contact person (2–120 characters).", "GUEST_ORDER_CONTACT_REQUIRED");

        var phoneDigits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (phoneDigits.Length is < 7 or > 15 ||
            phoneNumber.Any(c => !(char.IsDigit(c) || c is '+' or '-' or '(' or ')' or ' ')))
            throw new BusinessException("Enter a valid contact number.", "GUEST_ORDER_PHONE_INVALID");

        if (dto.Items == null || dto.Items.Count < 1)
            throw new BusinessException("Add at least one order item.", "GUEST_ORDER_ITEMS_INVALID");

        foreach (var item in dto.Items)
        {
            item.Description = Clean(item.Description);
            item.Size = Clean(item.Size);
            item.Brand = Clean(item.Brand);

            if (item.Description.Length is < 1 or > 200 || item.Quantity is < 1 or > 999999 ||
                item.Size?.Length > 80 || item.Brand?.Length > 100)
                throw new BusinessException("Check the description, size, brand and quantity for each item.", "GUEST_ORDER_ITEM_INVALID");
        }

        attachments ??= [];
        if (attachments.Count > 16)
            throw new BusinessException("You can attach up to 16 files.", "GUEST_ORDER_TOO_MANY_FILES");

        if (attachments.Sum(file => file.Length) > 50L * 1024 * 1024)
            throw new BusinessException("The combined attachment size cannot exceed 50 MB.", "GUEST_ORDER_FILES_TOO_LARGE");

        foreach (var file in attachments.Where(file => file.Length > 0))
            ValidateAttachment(file);

        var detailLines = new List<string>
        {
            "GUEST ORDER",
            $"Contact person: {contactPerson}",
            $"Contact number: {phoneNumber}",
            "Submitted via: Guest Order Form",
            string.Empty,
            "ORDER ITEMS",
        };

        for (var index = 0; index < dto.Items.Count; index++)
        {
            var item = dto.Items[index];
            detailLines.Add(
                $"{index + 1}. {item.Description} | Size: {(string.IsNullOrWhiteSpace(item.Size) ? "Not specified" : item.Size)} | Brand: {(string.IsNullOrWhiteSpace(item.Brand) ? "Not specified" : item.Brand)} | Quantity: {item.Quantity}");
        }

        var request = new QuickRequest
        {
            Id = Guid.NewGuid(),
            RequestNumber = await GenerateUniqueNumberAsync(QuickRequestType.Order, ct),
            Type = QuickRequestType.Order,
            CustomerName = businessName,
            Details = string.Join(Environment.NewLine, detailLines),
            RepId = null,
            CreatedBy = "Quick Order Portal",
        };

        foreach (var file in attachments.Where(file => file.Length > 0))
            request.Attachments.Add(await SaveAttachmentAsync(request.Id, file, ct));

        await _uow.Repository<QuickRequest>().AddAsync(request, ct);
        await _uow.SaveChangesAsync(ct);

        return MapToDto(request, string.Empty);
    }
    // ── Admin ─────────────────────────────────────────────────────────────────

    public async Task<QuickRequestDto> CreateAdminAsync(
        Guid adminUserId,
        string createdBy,
        CreateQuickRequestDto dto,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<QuickRequestType>(
                dto.Type,
                ignoreCase: true,
                out var type))
        {
            throw new BusinessException(
                "Select Order or Quotation.",
                "INVALID_QUICK_REQUEST_TYPE");
        }

        if (string.IsNullOrWhiteSpace(dto.CustomerName))
        {
            throw new BusinessException(
                "Customer name is required.",
                "QUICK_REQUEST_CUSTOMER_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(dto.Details))
        {
            throw new BusinessException(
                "Add at least one item.",
                "QUICK_REQUEST_DETAILS_REQUIRED");
        }

        var number = await GenerateUniqueNumberAsync(type, ct);

        var request = new QuickRequest
        {
            RequestNumber = number,
            Type = type,
            CustomerName = dto.CustomerName.Trim(),
            Details = dto.Details.Trim(),

            // Admin-created quick orders are not owned by a Sales Rep.
            RepId = null,

            CreatedBy = string.IsNullOrWhiteSpace(createdBy)
                ? adminUserId.ToString()
                : createdBy.Trim(),
        };

        await _uow.Repository<QuickRequest>().AddAsync(request, ct);
        await _uow.SaveChangesAsync(ct);

        return MapToDto(request, string.Empty);
    }

    public async Task<QuickRequestDto> AddAdminImagesAsync(
        Guid requestId,
        IList<IFormFile> images,
        CancellationToken ct = default)
    {
        var exists = await _uow.Repository<QuickRequest>().Query()
            .AsNoTracking()
            .AnyAsync(
                r =>
                    r.Id == requestId &&
                    r.Type == QuickRequestType.Order &&
                    !r.IsDeletedByAdmin &&
                    !r.IsPurgedByAdmin,
                ct);

        if (!exists)
            throw new NotFoundException("Quick request", requestId);

        var attachmentEntities =
            new List<QuickRequestAttachment>();

        foreach (var file in images)
        {
            if (file.Length == 0)
                continue;

            ValidateAttachment(file);

            attachmentEntities.Add(
                await SaveAttachmentAsync(requestId, file, ct));
        }

        if (attachmentEntities.Count > 0)
        {
            await _uow.Repository<QuickRequestAttachment>()
                .AddRangeAsync(attachmentEntities, ct);

            await _uow.SaveChangesAsync(ct);
        }

        var request = await _uow.Repository<QuickRequest>().Query()
            .AsNoTracking()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(
                r => r.Id == requestId,
                ct);

        if (request == null)
            throw new NotFoundException(
                "Quick request",
                requestId);

        return MapToDto(
            request,
            request.Rep?.FullName ?? string.Empty);
    }


    // Guest orders use the existing nullable RepId. Never change their source or status.
    private async Task<(bool IsAdmin, IQueryable<SalesRepProfile> Eligible)> GetAssignmentScopeAsync(Guid actorId, CancellationToken ct)
    {
        var actor = await _uow.Repository<User>().Query().FirstOrDefaultAsync(u => u.Id == actorId && u.IsActive, ct)
            ?? throw new ForbiddenException("An active account is required.");
        var eligible = _uow.Repository<SalesRepProfile>().Query()
            .Where(r => !r.IsDeleted && r.User.IsActive && r.User.Role == UserRole.SalesRep);
        if (actor.Role is UserRole.Admin or UserRole.SuperAdmin)
            return (true, eligible);
        if (actor.Role != UserRole.SalesCoordinator)
            throw new ForbiddenException("Only administrators and coordinators can manage guest order assignments.");
        var coordinator = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == actorId && !c.IsDeleted, ct)
            ?? throw new ForbiddenException("An active coordinator profile is required.");
        var memberships = _uow.Repository<RepCoordinator>().Query().Where(rc => rc.CoordinatorId == coordinator.Id);
        return (false, eligible.Where(r => memberships.Any(rc => rc.RepId == r.Id)));
    }

    private async Task<QuickRequest> GetAssignableGuestOrderAsync(Guid id, Guid actorId, bool isAdmin, CancellationToken ct)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Rep).Include(r => r.Images).Include(r => r.Attachments)
            .FirstOrDefaultAsync(r => r.Id == id && r.Type == QuickRequestType.Order &&
                r.CreatedBy == "Quick Order Portal" && !r.IsDeletedByAdmin && !r.IsPurgedByAdmin, ct)
            ?? throw new NotFoundException("Guest order", id);
        if (!isAdmin)
        {
            var coordinator = await _uow.Repository<CoordinatorProfile>().Query()
                .FirstAsync(c => c.UserId == actorId && !c.IsDeleted, ct);
            if (request.RepId.HasValue && !await _uow.Repository<RepCoordinator>().Query()
                .AnyAsync(rc => rc.CoordinatorId == coordinator.Id && rc.RepId == request.RepId.Value, ct))
                throw new ForbiddenException("This order is assigned to a sales rep outside your team.");
            if (await _uow.Repository<UserRecordTrash>().Query().AnyAsync(t => t.UserId == actorId &&
                t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == id, ct))
                throw new NotFoundException("Guest order", id);
        }
        return request;
    }

    public async Task<List<GuestOrderRepOptionDto>> GetGuestOrderRepOptionsAsync(Guid id, Guid actorId, CancellationToken ct = default)
    {
        var scope = await GetAssignmentScopeAsync(actorId, ct);
        await GetAssignableGuestOrderAsync(id, actorId, scope.IsAdmin, ct);
        return await scope.Eligible.OrderBy(r => r.FullName).ThenBy(r => r.EmployeeCode)
            .Select(r => new GuestOrderRepOptionDto { Id = r.Id, FullName = r.FullName, EmployeeCode = r.EmployeeCode })
            .ToListAsync(ct);
    }

    public async Task<QuickRequestDto> AssignGuestOrderRepAsync(Guid id, Guid actorId, AssignGuestOrderRepDto dto, string updatedBy, CancellationToken ct = default)
    {
        var scope = await GetAssignmentScopeAsync(actorId, ct);
        var request = await GetAssignableGuestOrderAsync(id, actorId, scope.IsAdmin, ct);
        if (request.RepId != dto.ExpectedRepId)
            throw new DbUpdateConcurrencyException("The sales rep assignment has changed.");
        SalesRepProfile? rep = null;
        if (dto.RepId.HasValue)
        {
            rep = await scope.Eligible.FirstOrDefaultAsync(r => r.Id == dto.RepId.Value, ct)
                ?? throw new ForbiddenException("This sales rep is inactive, unavailable, or outside your team.");
        }
        if (request.RepId != dto.RepId)
        {

            var notices = new List<Notification>();
            if (request.RepId.HasValue)
            {
                var formerRepId = request.RepId.Value;
                var formerUserId = await _uow.Repository<SalesRepProfile>().Query()
                    .Where(r => r.Id == formerRepId && r.User.IsActive && r.User.Role == UserRole.SalesRep)
                    .Select(r => (Guid?)r.UserId).FirstOrDefaultAsync(ct);
                if (formerUserId.HasValue)
                    notices.Add(new Notification
                    {
                        UserId = formerUserId.Value,
                        NotificationType = NotificationType.OrderStatusUpdate,
                        Title = "Order Assignment Removed",
                        Message = $"Guest order #{request.RequestNumber} is no longer assigned to you.",
                        Metadata = JsonSerializer.Serialize(new { kind = "GuestOrderAssignmentRemoved", requestNumber = request.RequestNumber })
                    });
            }
            if (rep != null)
                notices.Add(new Notification
                {
                    UserId = rep.UserId,
                    NotificationType = NotificationType.NewOrder,
                    Title = "New Order Assigned",
                    Message = $"Guest order #{request.RequestNumber} from {request.CustomerName} has been assigned to you.",
                    Metadata = JsonSerializer.Serialize(new { kind = "GuestOrderAssigned", quickRequestId = request.Id, requestNumber = request.RequestNumber })
                });
            // Persist notifications and assignment together; publish only after successful commit.
            foreach (var notice in notices)
                await _uow.Repository<Notification>().AddAsync(notice, ct);
            request.RepId = dto.RepId;
            request.Rep = rep;
            request.UpdatedAt = DateTime.UtcNow;
            request.UpdatedBy = updatedBy;
            await _uow.SaveChangesAsync(ct);

            if (_publisher != null)
                foreach (var notice in notices)
                    try
                    {
                        await _publisher.PublishToUserAsync(notice.UserId, NotificationService.MapToDto(notice), CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        // Assignment and unread notification are already saved. Do not report a failed assignment.
                        _logger?.LogWarning(ex, "Could not publish guest order assignment notification {NotificationId}", notice.Id);
                    }
        }
        return MapToDto(request, rep?.FullName ?? string.Empty);
    }

    public async Task<List<QuickRequestDto>> GetAllAsync(
        string? type = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .Where(r =>
                !r.IsDeletedByAdmin &&
                !r.IsPurgedByAdmin)
            .AsQueryable();

        if (!string.IsNullOrEmpty(type) &&
            Enum.TryParse<QuickRequestType>(
                type,
                ignoreCase: true,
                out var t))
        {
            query = query.Where(r => r.Type == t);
        }

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<QuickRequestStatus>(
                status,
                ignoreCase: true,
                out var s))
        {
            query = query.Where(r => r.Status == s);
        }

        var list = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return list
            .Select(r => MapToDto(
                r,
                r.Rep?.FullName ?? string.Empty))
            .ToList();
    }

    public async Task<QuickRequestDto> GetByIdAsync(
        Guid requestId,
        CancellationToken ct = default,
        bool includeAdminTrash = false)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(
                r => r.Id == requestId && !r.IsPurgedByAdmin &&
                    (!r.IsDeletedByAdmin || includeAdminTrash),
                ct)
            ?? throw new NotFoundException(
                "Quick request",
                requestId);

        return MapToDto(
            request,
            request.Rep?.FullName ?? string.Empty);
    }

    /// <summary>
    /// Coordinator-scoped variant of <see cref="GetByIdAsync"/> —
    /// throws NotFoundException if the request's rep isn't assigned
    /// to this coordinator.
    /// </summary>
    public async Task<QuickRequestDto> GetForCoordinatorByIdAsync(
        Guid requestId,
        Guid coordinatorUserId,
        CancellationToken ct = default,
        bool includePersonalTrash = false)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(
                c => c.UserId == coordinatorUserId,
                ct)
            ?? throw new NotFoundException(
                "Coordinator profile",
                coordinatorUserId);

        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(
                r =>
                    r.Id == requestId &&
                    (r.RepId.HasValue && repIds.Contains(r.RepId.Value) || !r.RepId.HasValue && r.CreatedBy == "Quick Order Portal") &&
                    !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                    !personalTrash.Any(t => t.UserId == coordinatorUserId &&
                        t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == r.Id &&
                        (!includePersonalTrash || t.PurgedAt != null)),
                ct)
            ?? throw new NotFoundException(
                "Quick request",
                requestId);

        return MapToDto(
            request,
            request.Rep?.FullName ?? string.Empty);
    }

    public async Task<QuickRequestDto> UpdateStatusAsync(
        Guid requestId,
        UpdateQuickRequestStatusDto dto,
        string updatedBy,
        CancellationToken ct = default)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(
                r => r.Id == requestId && !r.IsDeletedByAdmin && !r.IsPurgedByAdmin,
                ct)
            ?? throw new NotFoundException(
                "Quick request",
                requestId);

        if (!Enum.TryParse<QuickRequestStatus>(
                dto.Status,
                ignoreCase: true,
                out var newStatus))
        {
            throw new ArgumentException(
                $"Invalid status '{dto.Status}'.");
        }

        request.Status = newStatus;
        request.AdminNotes = dto.AdminNotes;
        request.UpdatedAt = DateTime.UtcNow;
        request.UpdatedBy = updatedBy;

        await _uow.SaveChangesAsync(ct);

        return MapToDto(
            request,
            request.Rep?.FullName ?? string.Empty);
    }

    public Task DeleteAsync(Guid requestId, CancellationToken ct = default)
    {
        // Physical deletion bypasses Admin Trash retention and would orphan audit references.
        throw new BusinessException(
            "Physical quick-request deletion is disabled. Use the Admin Trash workflow.",
            "QUICK_REQUEST_HARD_DELETE_DISABLED");
    }

    // ── Admin soft-delete / trash ─────────────────────────────────────────────

    public async Task AdminSoftDeleteAsync(
        Guid requestId,
        string deletedBy,
        CancellationToken ct = default)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(
                r =>
                    r.Id == requestId &&
                    !r.IsDeletedByAdmin &&
                    !r.IsPurgedByAdmin,
                ct)
            ?? throw new NotFoundException(
                "Quick request",
                requestId);

        request.IsDeletedByAdmin = true;
        request.AdminDeletedAt = DateTime.UtcNow;
        request.UpdatedBy = deletedBy;

        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<QuickRequestDto>> AdminGetTrashAsync(
        string? type = null,
        CancellationToken ct = default)
    {
        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .Where(r =>
                r.IsDeletedByAdmin &&
                !r.IsPurgedByAdmin);

        if (!string.IsNullOrEmpty(type) &&
            Enum.TryParse<QuickRequestType>(
                type,
                ignoreCase: true,
                out var t))
        {
            query = query.Where(r => r.Type == t);
        }

        var list = await query
            .OrderByDescending(r => r.AdminDeletedAt)
            .ToListAsync(ct);

        return list
            .Select(r => MapToDto(
                r,
                r.Rep?.FullName ?? string.Empty))
            .ToList();
    }

    public async Task AdminPurgeAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(r => r.Id == requestId &&
                r.IsDeletedByAdmin && !r.IsPurgedByAdmin, ct)
            ?? throw new NotFoundException("Quick request", requestId);
        // Keep the request and stored files for audit; remove it from Admin Trash only.
        AdminRecordTrashPurge.MarkQuickRequest(request, DateTime.UtcNow);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task AdminRestoreAsync(
        Guid requestId,
        CancellationToken ct = default)
    {
        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(
                r =>
                    r.Id == requestId &&
                    r.IsDeletedByAdmin &&
                    !r.IsPurgedByAdmin,
                ct)
            ?? throw new NotFoundException(
                "Quick request",
                requestId);

        request.IsDeletedByAdmin = false;
        request.AdminDeletedAt = null;

        await _uow.SaveChangesAsync(ct);
    }

    // ── Rep soft-delete / trash ────────────────────────────────────────────────

    public async Task RepSoftDeleteAsync(
        Guid requestId,
        Guid repUserId,
        CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(
                r => r.UserId == repUserId,
                ct)
            ?? throw new NotFoundException(
                "Sales rep profile",
                repUserId);

        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(
                r => r.Id == requestId && r.RepId == rep.Id &&
                    !r.IsDeletedByAdmin && !r.IsPurgedByAdmin,
                ct)
            ?? throw new NotFoundException("Quick request", requestId);

        await new PersonalRecordTrash(_uow).HideAsync(
            repUserId, PersonalRecordTrash.QuickRequestKind, requestId, ct);
    }

    public async Task RepPurgeAsync(Guid requestId, Guid repUserId, CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, ct)
            ?? throw new NotFoundException("Sales rep profile", repUserId);
        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var allowed = await _uow.Repository<QuickRequest>().Query().AnyAsync(r =>
            r.Id == requestId && r.RepId == rep.Id && !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
            personalTrash.Any(t => t.UserId == repUserId &&
                t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == r.Id && t.PurgedAt == null), ct);
        if (!allowed) throw new NotFoundException("Trash entry", requestId);
        await new PersonalRecordTrash(_uow).PermanentlyHideAsync(
            repUserId, PersonalRecordTrash.QuickRequestKind, requestId, ct);
    }

    public async Task<List<QuickRequestDto>> RepGetTrashAsync(
        Guid repUserId,
        string? type = null,
        CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(
                r => r.UserId == repUserId,
                ct)
            ?? throw new NotFoundException(
                "Sales rep profile",
                repUserId);

        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .Where(r =>
                r.RepId == rep.Id &&
                !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                personalTrash.Any(t => t.UserId == repUserId &&
                    t.RecordKind == PersonalRecordTrash.QuickRequestKind &&
                    t.RecordId == r.Id && t.PurgedAt == null));

        if (!string.IsNullOrEmpty(type) &&
            Enum.TryParse<QuickRequestType>(
                type,
                ignoreCase: true,
                out var t))
        {
            query = query.Where(r => r.Type == t);
        }

        var list = await query
            .OrderByDescending(r => personalTrash.Where(t => t.UserId == repUserId &&
                t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == r.Id)
                .Select(t => t.DeletedAt).FirstOrDefault())
            .ToListAsync(ct);

        var deletionDates = await personalTrash.Where(t => t.UserId == repUserId
                && t.RecordKind == PersonalRecordTrash.QuickRequestKind &&
                   list.Select(r => r.Id).Contains(t.RecordId))
            .ToDictionaryAsync(t => t.RecordId, t => t.DeletedAt, ct);

        return list.Select(r => MapToDto(
            r, r.Rep?.FullName ?? string.Empty, deletionDates.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task RepRestoreAsync(
        Guid requestId,
        Guid repUserId,
        CancellationToken ct = default)
    {
        var rep = await _uow.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(
                r => r.UserId == repUserId,
                ct)
            ?? throw new NotFoundException(
                "Sales rep profile",
                repUserId);

        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(
                r => r.Id == requestId && r.RepId == rep.Id &&
                    !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                    personalTrash.Any(t => t.UserId == repUserId &&
                        t.RecordKind == PersonalRecordTrash.QuickRequestKind &&
                        t.RecordId == r.Id && t.PurgedAt == null),
                ct)
            ?? throw new NotFoundException("Quick request", requestId);

        await new PersonalRecordTrash(_uow).RestoreAsync(
            repUserId, PersonalRecordTrash.QuickRequestKind, requestId, ct);
    }

    // ── Coordinator get ───────────────────────────────────────────────────────

    public async Task CoordinatorSoftDeleteAsync(
        Guid requestId,
        Guid coordinatorUserId,
        CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(
                c => c.UserId == coordinatorUserId,
                ct)
            ?? throw new NotFoundException(
                "Coordinator profile",
                coordinatorUserId);

        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(
                r =>
                    r.Id == requestId &&
                    (r.RepId.HasValue && repIds.Contains(r.RepId.Value) || !r.RepId.HasValue && r.CreatedBy == "Quick Order Portal") &&
                    !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                    !personalTrash.Any(t => t.UserId == coordinatorUserId &&
                        t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == r.Id),
                ct)
            ?? throw new NotFoundException(
                "Quick request",
                requestId);

        await new PersonalRecordTrash(_uow).HideAsync(
            coordinatorUserId, PersonalRecordTrash.QuickRequestKind, requestId, ct);
    }

    public async Task<List<QuickRequestDto>> GetForCoordinatorAsync(
        Guid coordinatorUserId,
        string? type = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(
                c => c.UserId == coordinatorUserId,
                ct)
            ?? throw new NotFoundException(
                "Coordinator profile",
                coordinatorUserId);

        // Coordinator sees quick requests from reps assigned to them
        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .Where(r =>
                (r.RepId.HasValue && repIds.Contains(r.RepId.Value) || !r.RepId.HasValue && r.CreatedBy == "Quick Order Portal") &&
                !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                !personalTrash.Any(t => t.UserId == coordinatorUserId &&
                    t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == r.Id));

        if (!string.IsNullOrEmpty(type) &&
            Enum.TryParse<QuickRequestType>(
                type,
                ignoreCase: true,
                out var t))
        {
            query = query.Where(r => r.Type == t);
        }

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<QuickRequestStatus>(
                status,
                ignoreCase: true,
                out var s))
        {
            query = query.Where(r => r.Status == s);
        }

        var list = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return list
            .Select(r => MapToDto(
                r,
                r.Rep?.FullName ?? string.Empty))
            .ToList();
    }

    public async Task<QuickRequestDto> CoordinatorUpdateStatusAsync(
        Guid requestId,
        UpdateQuickRequestStatusDto dto,
        Guid coordinatorUserId,
        CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(
                c => c.UserId == coordinatorUserId,
                ct)
            ?? throw new NotFoundException(
                "Coordinator profile",
                coordinatorUserId);

        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var request = await _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .FirstOrDefaultAsync(
                r =>
                    r.Id == requestId &&
                    (r.RepId.HasValue && repIds.Contains(r.RepId.Value) || !r.RepId.HasValue && r.CreatedBy == "Quick Order Portal") && !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                    !personalTrash.Any(t => t.UserId == coordinatorUserId &&
                        t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == r.Id),
                ct)
            ?? throw new NotFoundException(
                "Quick request",
                requestId);

        if (!Enum.TryParse<QuickRequestStatus>(
                dto.Status,
                ignoreCase: true,
                out var newStatus))
        {
            throw new ArgumentException(
                $"Invalid status '{dto.Status}'.");
        }

        request.Status = newStatus;
        request.UpdatedAt = DateTime.UtcNow;

        await _uow.SaveChangesAsync(ct);

        return MapToDto(
            request,
            request.Rep?.FullName ?? string.Empty);
    }

    public async Task CoordinatorPurgeAsync(Guid requestId, Guid coordinatorUserId, CancellationToken ct = default)
    {
        var coordinator = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);
        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coordinator.Id).Select(rc => rc.RepId).ToListAsync(ct);
        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var allowed = await _uow.Repository<QuickRequest>().Query().AnyAsync(r =>
            r.Id == requestId && (r.RepId.HasValue && repIds.Contains(r.RepId.Value) || !r.RepId.HasValue && r.CreatedBy == "Quick Order Portal") &&
            !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
            personalTrash.Any(t => t.UserId == coordinatorUserId &&
                t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == r.Id && t.PurgedAt == null), ct);
        if (!allowed) throw new NotFoundException("Trash entry", requestId);
        await new PersonalRecordTrash(_uow).PermanentlyHideAsync(
            coordinatorUserId, PersonalRecordTrash.QuickRequestKind, requestId, ct);
    }

    public async Task<List<QuickRequestDto>> CoordinatorGetTrashAsync(
        Guid coordinatorUserId,
        string? type = null,
        CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(
                c => c.UserId == coordinatorUserId,
                ct)
            ?? throw new NotFoundException(
                "Coordinator profile",
                coordinatorUserId);

        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var query = _uow.Repository<QuickRequest>().Query()
            .Include(r => r.Images)
            .Include(r => r.Attachments)
            .Include(r => r.Rep)
            .Where(r =>
                (r.RepId.HasValue && repIds.Contains(r.RepId.Value) || !r.RepId.HasValue && r.CreatedBy == "Quick Order Portal") &&
                !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                personalTrash.Any(t => t.UserId == coordinatorUserId &&
                    t.RecordKind == PersonalRecordTrash.QuickRequestKind &&
                    t.RecordId == r.Id && t.PurgedAt == null));

        if (!string.IsNullOrEmpty(type) &&
            Enum.TryParse<QuickRequestType>(
                type,
                ignoreCase: true,
                out var t))
        {
            query = query.Where(r => r.Type == t);
        }

        var list = await query
            .OrderByDescending(r => personalTrash.Where(t => t.UserId == coordinatorUserId &&
                t.RecordKind == PersonalRecordTrash.QuickRequestKind && t.RecordId == r.Id)
                .Select(t => t.DeletedAt).FirstOrDefault())
            .ToListAsync(ct);

        var deletionDates = await personalTrash.Where(t => t.UserId == coordinatorUserId
                && t.RecordKind == PersonalRecordTrash.QuickRequestKind &&
                   list.Select(r => r.Id).Contains(t.RecordId))
            .ToDictionaryAsync(t => t.RecordId, t => t.DeletedAt, ct);

        return list.Select(r => MapToDto(
            r, r.Rep?.FullName ?? string.Empty, deletionDates.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task CoordinatorRestoreAsync(
        Guid requestId,
        Guid coordinatorUserId,
        CancellationToken ct = default)
    {
        var coord = await _uow.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(
                c => c.UserId == coordinatorUserId,
                ct)
            ?? throw new NotFoundException(
                "Coordinator profile",
                coordinatorUserId);

        var repIds = await _uow.Repository<RepCoordinator>().Query()
            .Where(rc => rc.CoordinatorId == coord.Id)
            .Select(rc => rc.RepId)
            .ToListAsync(ct);

        var personalTrash = _uow.Repository<UserRecordTrash>().Query();
        var request = await _uow.Repository<QuickRequest>().Query()
            .FirstOrDefaultAsync(
                r =>
                    r.Id == requestId &&
                    (r.RepId.HasValue && repIds.Contains(r.RepId.Value) || !r.RepId.HasValue && r.CreatedBy == "Quick Order Portal") &&
                    !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                    personalTrash.Any(t => t.UserId == coordinatorUserId &&
                        t.RecordKind == PersonalRecordTrash.QuickRequestKind &&
                        t.RecordId == r.Id && t.PurgedAt == null),
                ct)
            ?? throw new NotFoundException(
                "Quick request",
                requestId);

        await new PersonalRecordTrash(_uow).RestoreAsync(
            coordinatorUserId, PersonalRecordTrash.QuickRequestKind, requestId, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();

    private static DateTime? AsUtc(DateTime? value) =>
        value.HasValue
            ? AsUtc(value.Value)
            : null;

    private static void ValidateAttachment(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);

        var isPdf =
            string.Equals(
                file.ContentType,
                "application/pdf",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                extension,
                ".pdf",
                StringComparison.OrdinalIgnoreCase);

        var isImage =
            AllowedImageTypes.Contains(file.ContentType) &&
            AllowedImageExtensions.Contains(extension);

        if (!isPdf && !isImage)
        {
            throw new BusinessException(
                "File must be a PNG, JPG, JPEG, WEBP or PDF file.",
                "INVALID_QUICK_REQUEST_ATTACHMENT_TYPE");
        }

        if (isPdf)
        {
            if (file.Length > MaxPdfSize)
            {
                throw new BusinessException(
                    "PDF size cannot exceed 10 MB.",
                    "QUICK_REQUEST_PDF_TOO_LARGE");
            }

            return;
        }

        if (file.Length > MaxImageSize)
        {
            throw new BusinessException(
                "Image size cannot exceed 5 MB.",
                "QUICK_REQUEST_IMAGE_TOO_LARGE");
        }
    }

    private async Task<QuickRequestAttachment> SaveAttachmentAsync(
        Guid requestId,
        IFormFile file,
        CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName);

        var isPdf = string.Equals(
            extension,
            ".pdf",
            StringComparison.OrdinalIgnoreCase);

        var allowedExtensions = isPdf
            ? AllowedPdfExtensions
            : AllowedImageExtensions;

        var allowedContentTypes = isPdf
            ? AllowedPdfTypes
            : AllowedImageTypes;

        var maxSize = isPdf
            ? MaxPdfSize
            : MaxImageSize;

        var saved = await _fileStorage.SaveAsync(
            file,
            "quick-requests",
            FileAccessCategory.Private,
            allowedExtensions,
            allowedContentTypes,
            maxSize,
            ct);

        return new QuickRequestAttachment
        {
            QuickRequestId = requestId,
            StorageKey = saved.StorageKey,
            OriginalFileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType,
            SizeBytes = file.Length,
        };
    }

    /// <summary>
    /// Resolves the storage key for one image or attachment of a request
    /// the caller has already been confirmed to have access to.
    /// Returns null if the file doesn't belong to that request.
    /// </summary>
    public async Task<string?> GetImageStorageKeyAsync(
        Guid requestId,
        Guid imageId,
        CancellationToken ct = default)
    {
        // Existing/legacy image record
        var image = await _uow.Repository<QuickRequestImage>().Query()
            .FirstOrDefaultAsync(
                i =>
                    i.Id == imageId &&
                    i.QuickRequestId == requestId,
                ct);

        if (image != null)
            return image.ImageUrl;

        // New generic attachment record
        var attachment =
            await _uow.Repository<QuickRequestAttachment>().Query()
                .FirstOrDefaultAsync(
                    a =>
                        a.Id == imageId &&
                        a.QuickRequestId == requestId,
                    ct);

        return attachment?.StorageKey;
    }

    private static QuickRequestDto MapToDto(
        QuickRequest r,
        string repName,
        DateTime? personalDeletedAt = null) => new()
    {
        Id = r.Id,
        RequestNumber = r.RequestNumber,
        Type = r.Type.ToString(),
        CustomerName = r.CustomerName,
        Details = r.Details,
        Status = r.Status.ToString(),
        AdminNotes = r.AdminNotes,
        RepId = r.RepId,
        RepName = repName,
        CreatedBy = r.CreatedBy,

        // Backward-compatible image URLs.
        // Existing legacy images + newly uploaded image attachments.
        ImageUrls = r.Images
            .Select(i =>
                $"/api/quick-requests/{r.Id}/images/{i.Id}")
            .Concat(
                r.Attachments
                    .Where(a =>
                        a.ContentType.StartsWith(
                            "image/",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(a =>
                        $"/api/quick-requests/{r.Id}/images/{a.Id}")
            )
            .ToList(),

        // New generic attachment list.
        // Legacy images are also exposed here so the frontend
        // can display all files through one collection.
        Attachments =
            r.Images
                .Select(i => new QuickRequestAttachmentDto
                {
                    Id = i.Id,
                    Url =
                        $"/api/quick-requests/{r.Id}/images/{i.Id}",
                    OriginalFileName = $"image-{i.Id}",
                    ContentType = "image/*",
                    SizeBytes = 0,
                    UploadedAt = i.UploadedAt,
                })
                .Concat(
                    r.Attachments.Select(a =>
                        new QuickRequestAttachmentDto
                        {
                            Id = a.Id,
                            Url =
                                $"/api/quick-requests/{r.Id}/images/{a.Id}",
                            OriginalFileName =
                                a.OriginalFileName,
                            ContentType =
                                a.ContentType,
                            SizeBytes =
                                a.SizeBytes,
                            UploadedAt =
                                a.UploadedAt,
                        })
                )
                .OrderBy(a => a.UploadedAt)
                .ToList(),

        CreatedAt = AsUtc(r.CreatedAt),
        UpdatedAt = AsUtc(r.UpdatedAt),

        DeletedAt = AsUtc(
            personalDeletedAt ??
            r.AdminDeletedAt ??
            r.CoordinatorDeletedAt ??
            r.RepDeletedAt),
    };
}
