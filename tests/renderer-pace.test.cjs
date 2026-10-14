const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = fs.readFileSync(path.join(__dirname, "../src/renderer/renderer.js"), "utf8");
const context = {
  localStorage: { getItem: () => null },
  document: { getElementById: () => ({}), addEventListener: () => {} },
  window: {}
};
vm.runInNewContext(source + "\nglobalThis.testPace = calculateWeeklyPace;", context);

function check(usedPercent, elapsedHours, expectedBudget, expectedBalance, expectedRisk) {
  const resetAt = new Date(Date.now() + (168 - elapsedHours) * 3600000).toISOString();
  const pace = context.testPace({
    secondary: { usedPercent, windowDurationMins: 10080, resetsAt: resetAt }
  });
  assert.equal(pace.safeLimitPercent, expectedBudget);
  assert.ok(Math.abs(pace.budgetBalancePercent - expectedBalance) < 0.001);
  assert.equal(pace.risk, expectedRisk);
}

check(10, 23, 20, 10, "safe");
check(15, 23, 20, 5, "warn");
check(20, 23, 20, 0, "alert");
check(10, 25, 40, 30, "safe");
check(39, 25, 40, 1, "warn");
check(40, 25, 40, 0, "alert");
check(41, 25, 40, -1, "alert");
check(99, 121, 100, 1, "warn");
console.log("Renderer pace: 10% unused on day one gives 30% available on day two.");
