// Loads the AskRaffa 4.0 prototype engines (reference/raffa-data.js, renewal-case-v4.js,
// savings-quote-v4.js) into Node, as the browser would (window.RD / window.RC / window.SQ).
//
// These scripts regenerate the C# port's test fixtures from the prototype — the source of truth:
//   node backend/scripts/askraffa4/facts.js > backend/tests/Raffa.Renewals.Tests/Cases/Fixtures/contracts.json
//   node backend/scripts/askraffa4/golden.js en backend/tests/Raffa.Renewals.Tests/Cases/Fixtures/flows.json > backend/tests/Raffa.Renewals.Tests/Cases/Fixtures/golden-en.json
//   node backend/scripts/askraffa4/golden.js it backend/tests/Raffa.Renewals.Tests/Cases/Fixtures/flows.json > backend/tests/Raffa.Renewals.Tests/Cases/Fixtures/golden-it.json
//   node backend/scripts/askraffa4/sq.js > backend/tests/Raffa.Savings.Tests/Verifiable/Fixtures/sq-golden.json
// and the Italian catalogues: python3 backend/scripts/generate_case_i18n.py
const fs = require('fs'), vm = require('vm'), path = require('path');
const R = path.resolve(__dirname, '../../../docs/design/Raffa.ai/design_handoff_askraffa_v4/reference') + '/';
const win = {}; const ctx = { window: win }; vm.createContext(ctx);
for (const f of ['raffa-data.js', 'renewal-case-v4.js', 'savings-quote-v4.js']) vm.runInContext(fs.readFileSync(R + f, 'utf8'), ctx);
module.exports = win;
