#!/bin/bash
# Recompute the dashboard "Cash in Hand" KPI for a tenant straight from SQL,
# mirroring the formula in UI/app.js renderDashboard().
DB="${1:-SmmsDb_Aadya}"
PW=$(docker exec smms-api printenv ControlPlane__TenantConnectionTemplate | sed -n 's/.*Password=\([^;]*\).*/\1/p')
Q() { docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d "$DB" -h -1 -W -s "|" -Q "SET NOCOUNT ON; $1"; }

echo "=== DB: $DB ==="

echo "--- collections by status (count | SUM(Amount) | SUM(AmountPaid)) ---"
Q "SELECT Status, COUNT(*), SUM(Amount), SUM(AmountPaid) FROM Collections GROUP BY Status;"

echo "--- A. totalCol = SUM(Amount) where Status='Paid' ---"
Q "SELECT ISNULL(SUM(Amount),0) FROM Collections WHERE LOWER(Status)='paid';"

echo "--- B. totalInc = SUM(Amount) SocietyIncomes (not deleted) ---"
Q "SELECT ISNULL(SUM(Amount),0) FROM SocietyIncomes WHERE IsDeleted=0;"

echo "--- C. totalExp = SUM(Amount) Expenses (not deleted) ---"
Q "SELECT ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0;"

echo "--- D. memberFunded = SUM(Amount) Expenses with FundedByLiabilityId ---"
Q "SELECT ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0 AND FundedByLiabilityId IS NOT NULL;"

echo "--- E. repaidOut = Repaid settlements on liabilities that HAVE a funded expense ---"
Q "SELECT ISNULL(SUM(s.Amount),0) FROM SocietyLiabilitySettlements s JOIN SocietyLiabilities l ON l.Id=s.LiabilityId WHERE s.Method='Repaid' AND EXISTS (SELECT 1 FROM Expenses e WHERE e.IsDeleted=0 AND e.FundedByLiabilityId=l.Id);"

echo "--- E2. ALL Repaid settlements (for comparison) ---"
Q "SELECT ISNULL(SUM(s.Amount),0) FROM SocietyLiabilitySettlements s WHERE s.Method='Repaid';"

echo "--- liabilities ---"
Q "SELECT l.Id, l.ContributorName, l.MemberId, l.Amount, l.SettledAmount, l.Status, l.IsDeleted, (SELECT COUNT(*) FROM Expenses e WHERE e.IsDeleted=0 AND e.FundedByLiabilityId=l.Id) AS FundedExp FROM SocietyLiabilities l;"

echo "--- settlements ---"
Q "SELECT s.Id, s.LiabilityId, s.Date, s.Amount, s.Method, s.ExpenseId FROM SocietyLiabilitySettlements s;"

echo "--- expenses funded by a liability ---"
Q "SELECT Id, ExpenseDate, Category, Amount, FundedByLiabilityId, IsDeleted FROM Expenses WHERE FundedByLiabilityId IS NOT NULL;"

echo "--- F. CASH = A + B - (C - D) - E ---"
Q "SELECT (SELECT ISNULL(SUM(Amount),0) FROM Collections WHERE LOWER(Status)='paid') + (SELECT ISNULL(SUM(Amount),0) FROM SocietyIncomes WHERE IsDeleted=0) - ((SELECT ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0) - (SELECT ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0 AND FundedByLiabilityId IS NOT NULL)) - (SELECT ISNULL(SUM(s.Amount),0) FROM SocietyLiabilitySettlements s JOIN SocietyLiabilities l ON l.Id=s.LiabilityId WHERE s.Method='Repaid' AND EXISTS (SELECT 1 FROM Expenses e WHERE e.IsDeleted=0 AND e.FundedByLiabilityId=l.Id));"

echo "--- G. balance = A + B - C ---"
Q "SELECT (SELECT ISNULL(SUM(Amount),0) FROM Collections WHERE LOWER(Status)='paid') + (SELECT ISNULL(SUM(Amount),0) FROM SocietyIncomes WHERE IsDeleted=0) - (SELECT ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0);"

echo "--- applied migrations (last 6) ---"
Q "SELECT TOP 6 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;"
echo done
