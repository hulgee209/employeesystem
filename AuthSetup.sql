IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Admin')
    INSERT INTO Roles (RoleName) VALUES ('Admin');

IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'HR')
    INSERT INTO Roles (RoleName) VALUES ('HR');

IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Manager')
    INSERT INTO Roles (RoleName) VALUES ('Manager');

IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Employee')
    INSERT INTO Roles (RoleName) VALUES ('Employee');

/*
Create your first admin from the app's Users page after logging in with an
existing admin account, or insert one manually with a PasswordHasher-generated
PasswordHash.

This project uses Microsoft.AspNetCore.Identity.PasswordHasher<User>.
Do not store plain text passwords in PasswordHash.
*/
