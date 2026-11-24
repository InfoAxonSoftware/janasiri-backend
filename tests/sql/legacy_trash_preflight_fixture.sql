-- Synthetic legacy state only, in disposable local PostgreSQL.
CREATE TABLE "__EFMigrationsHistory" ("MigrationId" text);
CREATE TABLE "SalesRepProfiles" ("Id" int PRIMARY KEY, "UserId" int);
CREATE TABLE "CoordinatorProfiles" ("Id" int PRIMARY KEY, "UserId" int);
CREATE TABLE "CustomerProfiles" ("Id" int PRIMARY KEY, "UserId" int, "AssignedCoordinatorId" int);
CREATE TABLE "RepCoordinators" ("RepId" int, "CoordinatorId" int);
CREATE TABLE "Orders" (
"Id" int PRIMARY KEY,"CustomerId" int,"RepId" int,
"IsDeleted" boolean DEFAULT false,"DeletedAt" timestamptz,
"IsDeletedByRep" boolean DEFAULT false,"IsPurgedByRep" boolean DEFAULT false,
"IsDeletedByCoordinator" boolean DEFAULT false,"IsPurgedByCoordinator" boolean DEFAULT false,
"IsDeletedByCustomer" boolean DEFAULT false);
CREATE TABLE "QuickRequests" (
"Id" int PRIMARY KEY,"RepId" int,
"IsDeletedByAdmin" boolean DEFAULT false,"AdminDeletedAt" timestamptz,
"IsDeletedByRep" boolean DEFAULT false,"IsPurgedByRep" boolean DEFAULT false,
"IsDeletedByCoordinator" boolean DEFAULT false,"IsPurgedByCoordinator" boolean DEFAULT false);
CREATE TABLE "Quotations" (
"Id" int PRIMARY KEY,"CustomerId" int,"RepId" int,"CoordinatorId" int,
"IsDeleted" boolean DEFAULT false,"DeletedAt" timestamptz,
"IsDeletedByRep" boolean DEFAULT false,"IsDeletedByCoordinator" boolean DEFAULT false,
"IsDeletedByCustomer" boolean DEFAULT false);
CREATE TABLE "RepPayments" (
"Id" int PRIMARY KEY,"RepId" int,
"IsDeletedByAdmin" boolean DEFAULT false,"AdminDeletedAt" timestamptz,
"IsDeletedBySalesRep" boolean DEFAULT false,"IsDeletedByCoordinator" boolean DEFAULT false);
INSERT INTO "SalesRepProfiles" VALUES (1,101);
INSERT INTO "CoordinatorProfiles" VALUES (1,201),(2,202);
INSERT INTO "CustomerProfiles" VALUES (1,301,1),(2,302,NULL);
INSERT INTO "RepCoordinators" VALUES (1,1),(1,2);
-- The shared legacy Order delete touches two distinct current Coordinators;
-- the recipient via customer and rep mapping must not be double counted.
INSERT INTO "Orders" ("Id","CustomerId","RepId","IsDeletedByCoordinator")
VALUES (1,1,1,true),(2,2,NULL,true);
INSERT INTO "QuickRequests" ("Id","RepId","IsPurgedByCoordinator")
VALUES (3,1,true);
INSERT INTO "Quotations" ("Id","CustomerId","RepId","CoordinatorId","IsDeletedByCoordinator")
VALUES (4,1,1,1,true);
INSERT INTO "RepPayments" ("Id","RepId","IsDeletedByCoordinator")
VALUES (5,1,true);

-- Additional synthetic dates to exercise first-sweep personal Trash counts.
ALTER TABLE "Orders" ADD COLUMN "RepDeletedAt" text;
ALTER TABLE "Orders" ADD COLUMN "CoordinatorDeletedAt" text;
ALTER TABLE "Orders" ADD COLUMN "CustomerDeletedAt" text;
ALTER TABLE "QuickRequests" ADD COLUMN "RepDeletedAt" text;
ALTER TABLE "QuickRequests" ADD COLUMN "CoordinatorDeletedAt" text;
ALTER TABLE "Quotations" ADD COLUMN "RepDeletedAt" text;
ALTER TABLE "Quotations" ADD COLUMN "CoordinatorDeletedAt" text;
ALTER TABLE "Quotations" ADD COLUMN "CustomerDeletedAt" text;
ALTER TABLE "RepPayments" ADD COLUMN "SalesRepDeletedAt" text;
ALTER TABLE "RepPayments" ADD COLUMN "CoordinatorDeletedAt" text;
UPDATE "Orders" SET "IsDeletedByRep"=true, "RepDeletedAt"=datetime('now','-10 days') WHERE "Id"=1;
UPDATE "QuickRequests" SET "IsPurgedByRep"=true, "RepDeletedAt"=datetime('now','-10 days') WHERE "Id"=3;
UPDATE "Quotations" SET "IsDeletedByCustomer"=true WHERE "Id"=4;
UPDATE "RepPayments" SET "IsDeletedBySalesRep"=true, "SalesRepDeletedAt"=datetime('now','-9 days') WHERE "Id"=5;
