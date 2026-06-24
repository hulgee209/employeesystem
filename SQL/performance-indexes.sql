-- Performance Indexes for EmployeeSystem

-- Audit Logs Indexes
CREATE INDEX IX_AuditLogs_UserId ON [AuditLogs]([UserId]);
CREATE INDEX IX_AuditLogs_TableName_RecordId ON [AuditLogs]([TableName], [RecordId]);
CREATE INDEX IX_AuditLogs_CreatedAt_Desc ON [AuditLogs]([CreatedAt] DESC);

-- Employee Indexes
CREATE INDEX IX_Employees_DepartmentId_ManagerId_IsActive ON [Employees]([DepartmentId], [ManagerId], [IsActive]);

-- Payroll Indexes
CREATE INDEX IX_Payroll_EmployeeId_PayMonth_Desc ON [Payroll]([EmployeeId], [PayMonth] DESC);

-- Attendance Indexes
CREATE INDEX IX_Attendance_EmployeeId_AttendanceDate_Desc ON [Attendance]([EmployeeId], [AttendanceDate] DESC);

-- LeaveRequests Indexes
CREATE INDEX IX_LeaveRequests_Status_ApproverId ON [LeaveRequests]([Status], [ApproverId]);
CREATE INDEX IX_LeaveRequests_EmployeeId_StartDate ON [LeaveRequests]([EmployeeId], [StartDate]);

-- PerformanceReviews Indexes
CREATE INDEX IX_PerformanceReviews_ReviewerId_Status ON [PerformanceReviews]([ReviewerId], [Status]);
CREATE INDEX IX_PerformanceReviews_EmployeeId_ReviewDate_Desc ON [PerformanceReviews]([EmployeeId], [ReviewDate] DESC);

-- Notifications Indexes
CREATE INDEX IX_Notifications_UserId_IsRead ON [Notifications]([UserId], [IsRead]);

-- ChatSession Indexes
CREATE INDEX IX_ChatSessions_UserId_LastMessageAt_Desc ON [ChatSessions]([UserId], [LastMessageAt] DESC);

-- ChatMessage Indexes
CREATE INDEX IX_ChatMessages_SessionId_CreatedAt ON [ChatMessages]([SessionId], [CreatedAt]);

-- User Indexes
CREATE INDEX IX_Users_EmployeeId ON [Users]([EmployeeId]);
