using System.Data;
using System.Data.Common;
using System.Security.Claims;
using EmployeeSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeSystem.Services;

public interface IEmployeeAnalyticsService
{
    Task<IReadOnlyList<EmployeeGridRow>> GetEmployee360GridAsync(
        ClaimsPrincipal user,
        string? search,
        int? departmentId,
        int? positionId,
        bool? status,
        decimal? salaryMin,
        decimal? salaryMax,
        decimal? attendanceMin,
        decimal? attendanceMax,
        decimal? performanceMin,
        decimal? performanceMax,
        DateOnly? hireFrom,
        DateOnly? hireTo,
        int page = 1,
        int pageSize = 50);
    Task<int> GetEmployee360GridCountAsync(
        ClaimsPrincipal user,
        string? search,
        int? departmentId,
        int? positionId,
        bool? status,
        decimal? salaryMin,
        decimal? salaryMax,
        decimal? attendanceMin,
        decimal? attendanceMax,
        decimal? performanceMin,
        decimal? performanceMax,
        DateOnly? hireFrom,
        DateOnly? hireTo);
    Task<Employee360DetailsViewModel?> GetEmployee360DetailsAsync(ClaimsPrincipal user, int employeeId);
    Task<object> GetEmployeeChartAsync(ClaimsPrincipal user, int employeeId, string chartType);
    Task<ExecutiveDashboardViewModel> GetExecutiveDashboardAsync(ClaimsPrincipal user);
}

public class EmployeeAnalyticsService : IEmployeeAnalyticsService
{
    private readonly EmployeeDbContext _context;

    public EmployeeAnalyticsService(EmployeeDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<EmployeeGridRow>> GetEmployee360GridAsync(
        ClaimsPrincipal user,
        string? search,
        int? departmentId,
        int? positionId,
        bool? status,
        decimal? salaryMin,
        decimal? salaryMax,
        decimal? attendanceMin,
        decimal? attendanceMax,
        decimal? performanceMin,
        decimal? performanceMax,
        DateOnly? hireFrom,
        DateOnly? hireTo,
        int page = 1,
        int pageSize = 50)
    {
        _context.Database.SetCommandTimeout(120);

        if (page < 1) page = 1;
        if (pageSize < 10) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var baseQuery = BuildScopedEmployees(user)
            .AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.Position)
            .Include(e => e.Manager);

        var query = baseQuery.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lowered = search.Trim().ToLower();
            query = query.Where(e =>
                e.FirstName.ToLower().Contains(lowered) ||
                e.LastName.ToLower().Contains(lowered) ||
                (e.Email != null && e.Email.ToLower().Contains(lowered)) ||
                (e.Phone != null && e.Phone.ToLower().Contains(lowered)) ||
                e.Department.DepartmentName.ToLower().Contains(lowered) ||
                e.Position.PositionName.ToLower().Contains(lowered));
        }

        if (departmentId.HasValue && departmentId.Value > 0)
        {
            query = query.Where(e => e.DepartmentId == departmentId.Value);
        }

        if (positionId.HasValue && positionId.Value > 0)
        {
            query = query.Where(e => e.PositionId == positionId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(e => e.IsActive == status.Value);
        }

        query = ApplyRangeFilters(
            query,
            salaryMin,
            salaryMax,
            attendanceMin,
            attendanceMax,
            performanceMin,
            performanceMax,
            hireFrom,
            hireTo);

        var baseEmployees = await query
            .OrderBy(e => e.EmployeeId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.EmployeeId,
                e.FirstName,
                e.LastName,
                e.DepartmentId,
                DepartmentName = e.Department.DepartmentName,
                e.PositionId,
                PositionName = e.Position.PositionName,
                e.ManagerId,
                ManagerName = e.Manager.FirstName + " " + e.Manager.LastName,
                e.Phone,
                e.Email,
                e.HireDate,
                e.IsActive
            })
            .ToListAsync();

        var employeeIds = baseEmployees.Select(e => e.EmployeeId).ToArray();

        var attendanceDict = await _context.Attendance
            .AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId))
            .GroupBy(a => a.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                AttendanceRate = g.Count(a => a.Status == "Present" || a.Status == "Late") * 100.0m / g.Count(),
                LateCount = g.Count(a => a.Status == "Late"),
                AbsentCount = g.Count(a => a.Status == "Absent")
            })
            .ToDictionaryAsync(x => x.EmployeeId);

        var payrollDict = await _context.Payroll
            .AsNoTracking()
            .Where(p => employeeIds.Contains(p.EmployeeId))
            .GroupBy(p => p.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                AverageSalary = g.Average(p => (decimal?)p.NetSalary) ?? 0,
                LatestSalary = g.OrderByDescending(p => p.PayrollMonth).First().NetSalary ?? 0
            })
            .ToDictionaryAsync(x => x.EmployeeId);

        var performanceDict = await _context.PerformanceReviews
            .AsNoTracking()
            .Where(r => employeeIds.Contains(r.EmployeeId))
            .GroupBy(r => r.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                PerformanceScore = g.Average(r => (decimal?)r.Score)
            })
            .ToDictionaryAsync(x => x.EmployeeId);

        var leaveDays = await GetLeaveBalanceDictionaryAsync(employeeIds);
        var projectCounts = await GetCountDictionaryAsync("EmployeeProjects", "EmployeeId", employeeIds);
        var trainingCounts = await GetCountDictionaryAsync("EmployeeTraining", "EmployeeId", employeeIds);
        var assetCounts = await GetCountDictionaryAsync("EmployeeAssets", "EmployeeId", employeeIds);

        var result = baseEmployees.Select(e => new EmployeeGridRow
        {
            EmployeeId = e.EmployeeId,
            FirstName = e.FirstName,
            LastName = e.LastName,
            DepartmentId = e.DepartmentId,
            DepartmentName = e.DepartmentName,
            PositionId = e.PositionId,
            PositionName = e.PositionName,
            ManagerId = e.ManagerId,
            ManagerName = e.ManagerName,
            Phone = e.Phone,
            Email = e.Email,
            HireDate = e.HireDate,
            IsActive = e.IsActive,
            AverageSalary = payrollDict.TryGetValue(e.EmployeeId, out var payroll) ? payroll.AverageSalary : 0,
            LatestSalary = payrollDict.TryGetValue(e.EmployeeId, out payroll) ? payroll.LatestSalary : 0,
            AttendanceRate = attendanceDict.TryGetValue(e.EmployeeId, out var att) ? att.AttendanceRate : 0,
            LateCount = attendanceDict.TryGetValue(e.EmployeeId, out att) ? att.LateCount : 0,
            AbsentCount = attendanceDict.TryGetValue(e.EmployeeId, out att) ? att.AbsentCount : 0,
            PerformanceScore = performanceDict.TryGetValue(e.EmployeeId, out var perf) && perf.PerformanceScore.HasValue ? perf.PerformanceScore.Value : 0,
            LeaveBalance = leaveDays.GetValueOrDefault(e.EmployeeId),
            ProjectCount = projectCounts.GetValueOrDefault(e.EmployeeId),
            TrainingCount = trainingCounts.GetValueOrDefault(e.EmployeeId),
            AssetCount = assetCounts.GetValueOrDefault(e.EmployeeId)
        }).ToList();

        return result;
    }

    public Task<int> GetEmployee360GridCountAsync(
        ClaimsPrincipal user,
        string? search,
        int? departmentId,
        int? positionId,
        bool? status,
        decimal? salaryMin,
        decimal? salaryMax,
        decimal? attendanceMin,
        decimal? attendanceMax,
        decimal? performanceMin,
        decimal? performanceMax,
        DateOnly? hireFrom,
        DateOnly? hireTo)
    {
        var query = BuildScopedEmployees(user)
            .AsNoTracking()
            .Where(e =>
                string.IsNullOrWhiteSpace(search) ||
                e.FirstName.Contains(search) ||
                e.LastName.Contains(search) ||
                (e.Email != null && e.Email.Contains(search)) ||
                (e.Phone != null && e.Phone.Contains(search)) ||
                e.Department.DepartmentName.Contains(search) ||
                e.Position.PositionName.Contains(search));

        if (departmentId.HasValue && departmentId.Value > 0)
        {
            query = query.Where(e => e.DepartmentId == departmentId.Value);
        }

        if (positionId.HasValue && positionId.Value > 0)
        {
            query = query.Where(e => e.PositionId == positionId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(e => e.IsActive == status.Value);
        }

        query = ApplyRangeFilters(
            query,
            salaryMin,
            salaryMax,
            attendanceMin,
            attendanceMax,
            performanceMin,
            performanceMax,
            hireFrom,
            hireTo);

        return query.CountAsync();
    }

    private IQueryable<Employee> ApplyRangeFilters(
        IQueryable<Employee> query,
        decimal? salaryMin,
        decimal? salaryMax,
        decimal? attendanceMin,
        decimal? attendanceMax,
        decimal? performanceMin,
        decimal? performanceMax,
        DateOnly? hireFrom,
        DateOnly? hireTo)
    {
        if (hireFrom.HasValue)
        {
            query = query.Where(e => e.HireDate >= hireFrom.Value);
        }

        if (hireTo.HasValue)
        {
            query = query.Where(e => e.HireDate <= hireTo.Value);
        }

        if (salaryMin.HasValue || salaryMax.HasValue)
        {
            var salaryEmployees = _context.Payroll
                .AsNoTracking()
                .GroupBy(p => p.EmployeeId)
                .Select(g => new
                {
                    EmployeeId = g.Key,
                    Salary = g.OrderByDescending(p => p.PayrollMonth)
                        .Select(p => p.NetSalary ?? 0)
                        .FirstOrDefault()
                });

            if (salaryMin.HasValue)
            {
                salaryEmployees = salaryEmployees.Where(x => x.Salary >= salaryMin.Value);
            }

            if (salaryMax.HasValue)
            {
                salaryEmployees = salaryEmployees.Where(x => x.Salary <= salaryMax.Value);
            }

            query = query.Where(e => salaryEmployees.Select(x => x.EmployeeId).Contains(e.EmployeeId));
        }

        if (attendanceMin.HasValue || attendanceMax.HasValue)
        {
            var attendanceEmployees = _context.Attendance
                .AsNoTracking()
                .GroupBy(a => a.EmployeeId)
                .Select(g => new
                {
                    EmployeeId = g.Key,
                    Rate = g.Count(a => a.Status == "Present" || a.Status == "Late") * 100.0m / g.Count()
                });

            if (attendanceMin.HasValue)
            {
                attendanceEmployees = attendanceEmployees.Where(x => x.Rate >= attendanceMin.Value);
            }

            if (attendanceMax.HasValue)
            {
                attendanceEmployees = attendanceEmployees.Where(x => x.Rate <= attendanceMax.Value);
            }

            query = query.Where(e => attendanceEmployees.Select(x => x.EmployeeId).Contains(e.EmployeeId));
        }

        if (performanceMin.HasValue || performanceMax.HasValue)
        {
            var performanceEmployees = _context.PerformanceReviews
                .AsNoTracking()
                .GroupBy(r => r.EmployeeId)
                .Select(g => new
                {
                    EmployeeId = g.Key,
                    Score = g.Average(r => (decimal?)r.Score) ?? 0
                });

            if (performanceMin.HasValue)
            {
                performanceEmployees = performanceEmployees.Where(x => x.Score >= performanceMin.Value);
            }

            if (performanceMax.HasValue)
            {
                performanceEmployees = performanceEmployees.Where(x => x.Score <= performanceMax.Value);
            }

            query = query.Where(e => performanceEmployees.Select(x => x.EmployeeId).Contains(e.EmployeeId));
        }

        return query;
    }

    public async Task<Employee360DetailsViewModel?> GetEmployee360DetailsAsync(ClaimsPrincipal user, int employeeId)
    {
        if (!await CanAccessEmployeeAsync(user, employeeId))
        {
            return null;
        }

        var employee = await _context.Employees
            .AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => new Employee360DetailsViewModel
            {
                EmployeeId = e.EmployeeId,
                FirstName = e.FirstName,
                LastName = e.LastName,
                DepartmentName = e.Department.DepartmentName,
                PositionName = e.Position.PositionName,
                Phone = e.Phone,
                Email = e.Email,
                HireDate = e.HireDate
            })
            .FirstOrDefaultAsync();

        if (employee == null)
        {
            return null;
        }

        employee.Attendance = await _context.Attendance
            .AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.AttendanceDate)
            .Take(60)
            .Select(a => new AttendanceRowViewModel
            {
                AttendanceId = a.AttendanceId,
                EmployeeId = a.EmployeeId,
                EmployeeName = a.Employee.FirstName + " " + a.Employee.LastName,
                DepartmentName = a.Employee.Department.DepartmentName,
                AttendanceDate = a.AttendanceDate,
                CheckInTime = a.CheckInTime,
                CheckOutTime = a.CheckOutTime,
                Status = a.Status ?? "-"
            })
            .ToListAsync();

        employee.Payroll = await _context.Payroll
            .AsNoTracking()
            .Where(p => p.EmployeeId == employeeId)
            .OrderByDescending(p => p.PayrollMonth)
            .Take(24)
            .Select(p => new PayrollRowViewModel
            {
                PayrollId = p.PayrollId,
                EmployeeId = p.EmployeeId,
                EmployeeName = p.Employee.FirstName + " " + p.Employee.LastName,
                DepartmentName = p.Employee.Department.DepartmentName,
                PayrollMonth = p.PayrollMonth,
                BaseSalary = p.BaseSalary ?? 0,
                Bonus = p.Bonus ?? 0,
                Deductions = p.Deductions ?? 0,
                NetSalary = p.NetSalary ?? 0
            })
            .ToListAsync();

        employee.Performance = await _context.PerformanceReviews
            .AsNoTracking()
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.ReviewDate)
            .Take(24)
            .Select(r => new PerformanceRowViewModel
            {
                ReviewId = r.ReviewId,
                EmployeeId = r.EmployeeId,
                EmployeeName = r.Employee.FirstName + " " + r.Employee.LastName,
                DepartmentName = r.Employee.Department.DepartmentName,
                ReviewDate = r.ReviewDate,
                Score = r.Score,
                Reviewer = null,
                Comments = r.Comments
            })
            .ToListAsync();

        employee.Leave = await GetSimpleItemsAsync("LeaveRequests", employeeId, "LeaveType", "Status", "StartDate", "EndDate");
        employee.Projects = await GetSimpleItemsAsync("EmployeeProjects", employeeId, "ProjectId", "Role", "AssignedDate", "Status");
        employee.Training = await GetSimpleItemsAsync("EmployeeTraining", employeeId, "TrainingId", "Status", "CompletedDate", "Score");
        employee.Assets = await GetSimpleItemsAsync("EmployeeAssets", employeeId, "AssetId", "Status", "AssignedDate", "ReturnDate");
        employee.Scores = CalculateScores(employee);

        return employee;
    }

    public async Task<object> GetEmployeeChartAsync(ClaimsPrincipal user, int employeeId, string chartType)
    {
        if (!await CanAccessEmployeeAsync(user, employeeId))
        {
            return Array.Empty<object>();
        }

        return chartType switch
        {
            "attendance-trend" => await _context.Attendance.AsNoTracking()
                .Where(a => a.EmployeeId == employeeId)
                .GroupBy(a => new { a.AttendanceDate.Year, a.AttendanceDate.Month, a.Status })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Status, Count = g.Count() })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToListAsync(),

            "attendance-distribution" => await _context.Attendance.AsNoTracking()
                .Where(a => a.EmployeeId == employeeId)
                .GroupBy(a => a.Status)
                .Select(g => new { label = g.Key, value = (decimal)g.Count() })
                .ToListAsync(),

            "salary-trend" => await GetSalaryTrendChartAsync(employeeId),

            "salary-breakdown" => await _context.Payroll.AsNoTracking()
                .Where(p => p.EmployeeId == employeeId)
                .OrderByDescending(p => p.PayrollMonth)
                .Take(1)
                .Select(p => new[]
                {
                    new { label = "Үндсэн цалин", value = p.BaseSalary ?? 0 },
                    new { label = "Бонус", value = p.Bonus ?? 0 },
                    new { label = "Суутгал", value = p.Deductions ?? 0 }
                })
                .FirstOrDefaultAsync() ?? Array.Empty<object>(),

            "performance-trend" => await GetPerformanceTrendChartAsync(employeeId),

            "performance-distribution" => await GetPerformanceDistributionChartAsync(employeeId),

            "leave-statistics" => await GetGroupedSimpleChartAsync("LeaveRequests", employeeId, "Status"),
            "project-participation" => await GetGroupedSimpleChartAsync("EmployeeProjects", employeeId, "Status"),
            "training-completion" => await GetGroupedSimpleChartAsync("EmployeeTraining", employeeId, "Status"),
            "asset-assignment" => await GetGroupedSimpleChartAsync("EmployeeAssets", employeeId, "Status"),
            _ => Array.Empty<object>()
        };
    }

    public async Task<ExecutiveDashboardViewModel> GetExecutiveDashboardAsync(ClaimsPrincipal user)
    {
        var scoped = BuildScopedEmployees(user).AsNoTracking();
        var scopedEmployeeIds = scoped.Select(e => e.EmployeeId);

        var topDepartments = await scoped
            .GroupBy(e => e.Department.DepartmentName)
            .Select(g => new { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToListAsync();

        var highestPayrollCost = await _context.Payroll
            .AsNoTracking()
            .Where(p => scopedEmployeeIds.Contains(p.EmployeeId))
            .GroupBy(p => p.Employee.Department.DepartmentName)
            .Select(g => new { Label = g.Key, Value = g.Sum(p => p.NetSalary) ?? 0 })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToListAsync();

        var bestPerformingDepartments = await _context.PerformanceReviews
            .AsNoTracking()
            .Where(r => scopedEmployeeIds.Contains(r.EmployeeId))
            .GroupBy(r => r.Employee.Department.DepartmentName)
            .Select(g => new { Label = g.Key, Value = g.Average(r => (decimal?)r.Score) ?? 0 })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToListAsync();

        var mostAbsences = await _context.Attendance
            .AsNoTracking()
            .Where(a => a.Status == "Absent" && scopedEmployeeIds.Contains(a.EmployeeId))
            .GroupBy(a => a.Employee.Department.DepartmentName)
            .Select(g => new { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToListAsync();

        var employeeGrowthTrend = await scoped
            .Where(e => e.HireDate != null)
            .GroupBy(e => new { e.HireDate!.Value.Year, e.HireDate.Value.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Value = g.Count()
            })
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .Take(12)
            .ToListAsync();

        // compute KPI totals for executive summary
        var totalEmployees = await scoped.CountAsync();
        var totalDepartments = await _context.Departments.AsNoTracking().CountAsync();
        var totalPositions = await _context.Positions.AsNoTracking().CountAsync();
        var growthValue = employeeGrowthTrend.LastOrDefault()?.Value ?? 0;

        return new ExecutiveDashboardViewModel
        {
            TopDepartments = topDepartments
                .Select(x => new DashboardMetricItem { Label = x.Label, Value = x.Value })
                .ToList(),
            HighestPayrollCost = highestPayrollCost
                .Select(x => new DashboardMetricItem { Label = x.Label, Value = x.Value })
                .ToList(),
            BestPerformingDepartments = bestPerformingDepartments
                .Select(x => new DashboardMetricItem { Label = x.Label, Value = x.Value })
                .ToList(),
            MostAbsences = mostAbsences
                .Select(x => new DashboardMetricItem { Label = x.Label, Value = x.Value })
                .ToList(),
            MostActiveProjects = await GetGlobalGroupedChartAsync("EmployeeProjects", "ProjectId"),
            TrainingParticipation = await GetGlobalGroupedChartAsync("EmployeeTraining", "TrainingId"),
            EmployeeGrowthTrend = employeeGrowthTrend
                .Select(x => new DashboardMetricItem
                {
                    Label = $"{x.Year}-{x.Month:00}",
                    Value = x.Value
                })
                .ToList()
            ,
            TotalEmployees = totalEmployees,
            TotalDepartments = totalDepartments,
            TotalPositions = totalPositions,
            EmployeeGrowthValue = growthValue
        };
    }

    private async Task<IReadOnlyList<object>> GetSalaryTrendChartAsync(int employeeId)
    {
        var rows = await _context.Payroll
            .AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.PayrollMonth.HasValue)
            .OrderBy(p => p.PayrollMonth)
            .Select(p => new
            {
                p.PayrollMonth!.Value.Year,
                p.PayrollMonth.Value.Month,
                Value = p.NetSalary ?? 0
            })
            .ToListAsync();

        return rows
            .Select(p => new
            {
                label = $"{p.Year}-{p.Month:00}",
                value = p.Value
            })
            .ToList<object>();
    }

    private async Task<IReadOnlyList<object>> GetPerformanceTrendChartAsync(int employeeId)
    {
        var rows = await _context.PerformanceReviews
            .AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.ReviewDate.HasValue)
            .OrderBy(r => r.ReviewDate)
            .Select(r => new
            {
                r.ReviewDate!.Value.Year,
                r.ReviewDate.Value.Month,
                r.ReviewDate.Value.Day,
                Value = r.Score ?? 0
            })
            .ToListAsync();

        return rows
            .Select(r => new
            {
                label = $"{r.Year}-{r.Month:00}-{r.Day:00}",
                value = r.Value
            })
            .ToList<object>();
    }

    private async Task<IReadOnlyList<object>> GetPerformanceDistributionChartAsync(int employeeId)
    {
        var rows = await _context.PerformanceReviews
            .AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.Score.HasValue)
            .GroupBy(r => r.Score!.Value)
            .Select(g => new
            {
                Score = g.Key,
                Count = g.Count()
            })
            .OrderBy(x => x.Score)
            .ToListAsync();

        return rows
            .Select(x => new
            {
                label = x.Score.ToString(),
                value = (decimal)x.Count
            })
            .ToList<object>();
    }

    private IQueryable<Employee> BuildScopedEmployees(ClaimsPrincipal user)
    {
        return _context.Employees.AsQueryable();
    }

    private async Task<bool> CanAccessEmployeeAsync(ClaimsPrincipal user, int employeeId)
    {
        return await BuildScopedEmployees(user).AnyAsync(e => e.EmployeeId == employeeId);
    }

    private Employee360Scores CalculateScores(Employee360DetailsViewModel employee)
    {
        var absences = employee.Attendance.Count(a => a.Status == "Absent");
        var late = employee.Attendance.Count(a => a.Status == "Late");
        var attendanceRisk = Math.Clamp(absences * 12 + late * 5, 0, 100);
        var scoredReviews = employee.Performance
            .Where(r => r.Score.HasValue)
            .Select(r => r.Score!.Value)
            .ToList();
        var averagePerformance = scoredReviews.Count == 0
            ? 0m
            : (decimal)scoredReviews.Average();
        var performanceRisk = averagePerformance == 0 ? 50 : Math.Clamp((int)Math.Round((5m - averagePerformance) * 20m), 0, 100);
        var leaveDays = employee.Leave.Count;
        var health = Math.Clamp(100 - ((attendanceRisk + performanceRisk) / 2) - Math.Min(leaveDays * 2, 20), 0, 100);

        return new Employee360Scores
        {
            AttendanceRiskScore = attendanceRisk,
            PerformanceRiskScore = performanceRisk,
            EmployeeHealthScore = health,
            PromotionCandidateScore = Math.Clamp((int)Math.Round((averagePerformance * 16) + (health * .2m)), 0, 100),
            RetentionRiskScore = Math.Clamp((attendanceRisk + performanceRisk + Math.Min(leaveDays * 3, 30)) / 2, 0, 100),
            TrainingRecommendationScore = Math.Clamp(performanceRisk + Math.Max(0, 4 - employee.Training.Count) * 8, 0, 100),
            AttendanceForecast = attendanceRisk >= 70 ? "Ирцийн эрсдэл өсөх төлөвтэй" : attendanceRisk >= 35 ? "Ирцийг тогтмол ажиглах шаардлагатай" : "Ирц тогтвортой",
            PerformanceForecast = performanceRisk >= 70 ? "Гүйцэтгэл буурах эрсдэлтэй" : performanceRisk >= 35 ? "Дэмжлэг өгвөл сайжрах боломжтой" : "Гүйцэтгэл тогтвортой"
        };
    }

    private async Task<Dictionary<int, int>> GetCountDictionaryAsync(string table, string employeeColumn, int[] employeeIds)
    {
        return await GetIntDictionaryAsync(table, employeeColumn, null, employeeIds);
    }

    private async Task<Dictionary<int, int>> GetIntDictionaryAsync(string table, string employeeColumn, string? sumColumn, int[] employeeIds)
    {
        var result = new Dictionary<int, int>();

        if (employeeIds.Length == 0)
        {
            return result;
        }

        try
        {
            var connection = _context.Database.GetDbConnection();
            await using var _ = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            var parameters = employeeIds.Select((id, index) =>
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@p" + index;
                parameter.Value = id;
                command.Parameters.Add(parameter);
                return parameter.ParameterName;
            });

            var tableName = QuoteIdentifier(table);
            var employeeName = QuoteIdentifier(employeeColumn);
            var aggregation = string.IsNullOrWhiteSpace(sumColumn) ? "COUNT(1)" : $"SUM(COALESCE({QuoteIdentifier(sumColumn)}, 0))";
            command.CommandText = $"SELECT {employeeName}, CAST({aggregation} AS int) FROM {tableName} WHERE {employeeName} IN ({string.Join(",", parameters)}) GROUP BY {employeeName}";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result[Convert.ToInt32(reader.GetValue(0))] =
                    reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
            }
        }
        catch (DbException)
        {
            return result;
        }

        return result;
    }

    private async Task<IReadOnlyList<Employee360SimpleItem>> GetSimpleItemsAsync(
        string table,
        int employeeId,
        string titleColumn,
        string? statusColumn,
        string? dateColumn,
        string? detailColumn)
    {
        var items = new List<Employee360SimpleItem>();

        try
        {
            var connection = _context.Database.GetDbConnection();
            await using var _ = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            var employeeParameter = command.CreateParameter();
            employeeParameter.ParameterName = "@employeeId";
            employeeParameter.Value = employeeId;
            command.Parameters.Add(employeeParameter);
            command.CommandText = IsPostgres()
                ? $"""
                    SELECT
                        {CastAsText(titleColumn, 200)} AS Title,
                        {(statusColumn == null ? "NULL" : CastAsText(statusColumn, 200))} AS Status,
                        {(dateColumn == null ? "NULL" : CastAsText(dateColumn, 200))} AS DateText,
                        {(detailColumn == null ? "NULL" : CastAsText(detailColumn, 400))} AS Detail
                    FROM {QuoteIdentifier(table)}
                    WHERE {QuoteIdentifier("EmployeeId")} = @employeeId
                    ORDER BY 1 DESC
                    LIMIT 50
                    """
                : $"""
                    SELECT TOP 50
                        {CastAsText(titleColumn, 200)} AS Title,
                        {(statusColumn == null ? "NULL" : CastAsText(statusColumn, 200))} AS Status,
                        {(dateColumn == null ? "NULL" : CastAsText(dateColumn, 200))} AS DateText,
                        {(detailColumn == null ? "NULL" : CastAsText(detailColumn, 400))} AS Detail
                    FROM {QuoteIdentifier(table)}
                    WHERE {QuoteIdentifier("EmployeeId")} = @employeeId
                    ORDER BY 1 DESC
                    """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(new Employee360SimpleItem
                {
                    Title = reader.IsDBNull(0) ? "-" : reader.GetString(0),
                    Status = reader.IsDBNull(1) ? null : reader.GetString(1),
                    DateText = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Detail = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }
        }
        catch (DbException)
        {
            return items;
        }

        return items;
    }

    private async Task<IReadOnlyList<DashboardMetricItem>> GetGroupedSimpleChartAsync(string table, int employeeId, string groupColumn)
    {
        var result = new List<DashboardMetricItem>();

        try
        {
            var connection = _context.Database.GetDbConnection();
            await using var _ = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@employeeId";
            parameter.Value = employeeId;
            command.Parameters.Add(parameter);
            command.CommandText = $"SELECT {CastAsText(groupColumn, 200)}, COUNT(1) FROM {QuoteIdentifier(table)} WHERE {QuoteIdentifier("EmployeeId")} = @employeeId GROUP BY {QuoteIdentifier(groupColumn)}";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new DashboardMetricItem
                {
                    Label = reader.IsDBNull(0) ? "-" : reader.GetString(0),
                    Value = Convert.ToDecimal(reader.GetValue(1))
                });
            }
        }
        catch (DbException)
        {
            return result;
        }

        return result;
    }

    private async Task<IReadOnlyList<DashboardMetricItem>> GetGlobalGroupedChartAsync(string table, string groupColumn)
    {
        var result = new List<DashboardMetricItem>();

        try
        {
            var connection = _context.Database.GetDbConnection();
            await using var _ = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = IsPostgres()
                ? $"SELECT {CastAsText(groupColumn, 200)}, COUNT(1) FROM {QuoteIdentifier(table)} GROUP BY {QuoteIdentifier(groupColumn)} ORDER BY COUNT(1) DESC LIMIT 8"
                : $"SELECT TOP 8 {CastAsText(groupColumn, 200)}, COUNT(1) FROM {QuoteIdentifier(table)} GROUP BY {QuoteIdentifier(groupColumn)} ORDER BY COUNT(1) DESC";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new DashboardMetricItem
                {
                    Label = reader.IsDBNull(0) ? "-" : reader.GetString(0),
                    Value = Convert.ToDecimal(reader.GetValue(1))
                });
            }
        }
        catch (DbException)
        {
            return result;
        }

        return result;
    }

    private async Task<IAsyncDisposable> OpenConnectionAsync()
    {
        var connection = _context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
            return new ConnectionCloser(connection);
        }

        return new NoopAsyncDisposable();
    }

    private static bool IsEmployeeOnly(ClaimsPrincipal user)
    {
        return user.IsInRole("Employee") &&
            !user.IsInRole("Admin") &&
            !user.IsInRole("HR") &&
            !user.IsInRole("Manager");
    }

    private static bool TryGetEmployeeId(ClaimsPrincipal user, out int employeeId)
    {
        return int.TryParse(user.FindFirst("EmployeeId")?.Value, out employeeId);
    }

    private async Task<Dictionary<int, int>> GetLeaveBalanceDictionaryAsync(int[] employeeIds)
    {
        var result = new Dictionary<int, int>();

        if (employeeIds.Length == 0)
        {
            return result;
        }

        try
        {
            var connection = _context.Database.GetDbConnection();
            await using var _ = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            var parameters = employeeIds.Select((id, index) =>
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@p" + index;
                parameter.Value = id;
                command.Parameters.Add(parameter);
                return parameter.ParameterName;
            });

            command.CommandText = $"""
                SELECT {QuoteIdentifier("EmployeeId")}, CAST(SUM(COALESCE({QuoteIdentifier("Days")}, 0)) AS int)
                FROM {QuoteIdentifier("LeaveRequests")}
                WHERE {QuoteIdentifier("EmployeeId")} IN ({string.Join(",", parameters)})
                GROUP BY {QuoteIdentifier("EmployeeId")}
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result[Convert.ToInt32(reader.GetValue(0))] =
                    reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
            }
        }
        catch (DbException)
        {
            return result;
        }

        return result;
    }

    private sealed class ConnectionCloser : IAsyncDisposable
    {
        private readonly IDbConnection _connection;

        public ConnectionCloser(IDbConnection connection)
        {
            _connection = connection;
        }

        public ValueTask DisposeAsync()
        {
            _connection.Close();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private bool IsPostgres()
    {
        return _context.Database.IsNpgsql();
    }

    private string QuoteIdentifier(string identifier)
    {
        return IsPostgres()
            ? "\"" + identifier.Replace("\"", "\"\"") + "\""
            : "[" + identifier.Replace("]", "]]") + "]";
    }

    private string CastAsText(string column, int maxLength)
    {
        var quoted = QuoteIdentifier(column);
        return IsPostgres()
            ? $"CAST({quoted} AS text)"
            : $"CAST({quoted} AS nvarchar({maxLength}))";
    }
}
