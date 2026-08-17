// Records a silent walkthrough of the admin UI for demo/training videos.
//
//   npm i playwright && npx playwright install chromium
//   $env:SMMS_ADMIN_USER='...'; $env:SMMS_ADMIN_PASS='...'   # never commit these
//   node tools/record-walkthrough.mjs
//
// Output: tools/walkthrough/<timestamp>/*.webm
//
// Aadya is a live society, so every screen holding resident PII, bank details or contact
// numbers is blurred in the page itself before it is filmed - the masking is in the footage,
// not applied afterwards.

import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import path from 'node:path';

const BASE = process.env.SMMS_URL ?? 'https://aadya.yuvaansoft.shop/';
const USER = process.env.SMMS_ADMIN_USER;
const PASS = process.env.SMMS_ADMIN_PASS;
const HOLD = Number(process.env.SMMS_HOLD_MS ?? 4000);   // pause on each screen

if (!USER || !PASS) {
  console.error('Set SMMS_ADMIN_USER and SMMS_ADMIN_PASS first. Do not hard-code them.');
  process.exit(1);
}

// nth disambiguates the two nav entries both labelled "Collections".
const scenes = [
  { label: 'Dashboard', nth: 0, say: 'What came in, what went out, what we hold, what is owed.' },
  { label: 'Dashboard', nth: 0, scrollTo: 420, say: 'Trends, category split, and bills coming due.' },
  { label: 'Collections', nth: 1, say: 'Charges each flat pays. Fixed, per sq ft, per flat type or per tower.' },
  { label: 'Collections', nth: 0, say: '203 invoices this year; chase the seven unpaid.' },
  { label: 'Payments', nth: 0, say: 'UPI proofs awaiting approval, and statement auto-matching by UTR.' },
  { label: 'Expenses', nth: 0, say: 'Top row booked itself when the electricity bill was marked paid.' },
  { label: 'Liabilities', nth: 0, say: 'Money the society owes members who paid out of pocket.' },
  { label: 'Reimbursements', nth: 0, say: 'Where those claims start, with evidence attached.' },
  { label: 'Other Income', nth: 0, say: 'Anything that is not maintenance.' },
  { label: 'Budget Planner', nth: 0, say: 'Plan the month; the health card shows how long cash lasts.' },
  { label: 'Utility Connections', nth: 0, say: 'Bills fetch themselves; pay, paste the UTR, expense books.' },
  { label: 'Members', nth: 0, say: '25 flats, each with an advance wallet.' },
  { label: 'Complaints', nth: 0, say: 'Open, in progress, resolved.' },
  { label: 'Gate Log', nth: 0, say: 'Visitors and parcels.' },
  { label: 'Reports', nth: 0, say: 'Monthly summary, category split, defaulters.' },
  { label: 'Audit Log', nth: 0, say: 'Every change, attributed and timestamped.' },
  { label: 'Settings', nth: 0, say: 'Profile, billing schedule, late fees, categories, branding.' },
  { label: 'Admin', nth: 1, say: 'Accounts and per-module permissions.' }
];

// One stylesheet covers every screen: the app is a single page, so tab-scoped selectors keep
// applying as tabs re-render. Column numbers are 1-based against each table.
const MASK_CSS = `
  #upi-id, #upi-payee, #upi-bank, #upi-acname, #upi-acnum, #upi-ifsc,
  #tab-members  table tbody td:nth-child(2),
  #tab-members  table tbody td:nth-child(5),
  #tab-members  table tbody td:nth-child(6),
  #tab-liabilities table tbody td:nth-child(2),
  #tab-liabilities table tbody td:nth-child(9),
  #tab-reimb    table tbody td:nth-child(2),
  #tab-reimb    table tbody td:nth-child(6),
  #tab-gatelog  table tbody td:nth-child(3),
  #tab-gatelog  table tbody td:nth-child(6),
  #tab-gatelog  table tbody td:nth-child(7),
  #tab-auditlog table tbody td:nth-child(5),
  #tab-admin    table tbody td:nth-child(1),
  #tab-admin    table tbody td:nth-child(2),
  #tab-admin    table tbody td:nth-child(3),
  #tab-admin    table tbody td:nth-child(4),
  #tab-admin    table tbody td:nth-child(6)
  { filter: blur(7px) !important; }
`;

// The society profile fields carry a personal address, email and phone. They have no stable ids
// and the address is a textarea, so find them by their card rather than by selector.
async function maskSocietyProfile(page) {
  await page.evaluate(() => {
    const heading = [...document.querySelectorAll('h4, h3')]
      .find(h => h.textContent.includes('Society Profile'));
    if (!heading) return;
    heading.closest('div')?.querySelectorAll('input, textarea')
      .forEach(el => { if (el.type !== 'file') el.style.filter = 'blur(7px)'; });
  });
}

const stamp = new Date().toISOString().replace(/[:.]/g, '-');
const outDir = path.resolve('tools/walkthrough', stamp);
mkdirSync(outDir, { recursive: true });

const browser = await chromium.launch({ headless: false, slowMo: 120 });
const context = await browser.newContext({
  viewport: { width: 1600, height: 900 },
  recordVideo: { dir: outDir, size: { width: 1600, height: 900 } }
});
const page = await context.newPage();

try {
  console.log(`Recording ${BASE} -> ${outDir}`);
  await page.goto(new URL('login.html', BASE).href, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(2500);                       // hold on the public landing page

  await page.getByRole('button', { name: 'Admin Login' }).first().click();
  await page.waitForSelector('#login-username', { state: 'visible' });
  await page.fill('#login-username', USER);
  await page.fill('#login-password', PASS);
  await page.getByRole('button', { name: 'Login', exact: true }).click();

  await page.waitForSelector('.nav-item.admin-only', { timeout: 20000 });
  await page.waitForTimeout(2000);
  await page.addStyleTag({ content: MASK_CSS });

  for (const scene of scenes) {
    console.log(`  ${scene.label}${scene.nth ? ` [${scene.nth}]` : ''} - ${scene.say}`);
    await page.locator('.nav-item.admin-only', { hasText: scene.label }).nth(scene.nth).click();
    await page.waitForTimeout(1500);
    if (scene.label === 'Settings') await maskSocietyProfile(page);
    await page.evaluate(y => window.scrollTo({ top: y, behavior: 'smooth' }), scene.scrollTo ?? 0);
    await page.waitForTimeout(HOLD);
  }
}
finally {
  await context.close();   // the video is only flushed to disk when the context closes
  await browser.close();
  console.log(`\nDone. Video written to ${outDir}`);
  console.log('Review it before sharing - confirm every blurred panel actually rendered blurred.');
}
