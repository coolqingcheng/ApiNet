// Use the installed Electron application's V8 version to read its own serialized
// values. This script reads only the temporary snapshot and never starts its UI.
const fs = require('fs');
const v8 = require('v8');
const input = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const format = process.argv[4];
let failed = 0;
const records = [];
function relevant(value) {
    if (!value || typeof value !== 'object') return null;
    if (format === 'ApiPost') {
        if (value.target_id && value.target_type && !value.history_id && !value.result_id) return value;
        if (['default_server', 'global_var_list', 'env_var_list'].includes(value.key)) return value;
    } else {
        for (const candidate of [value, value.value, value.data]) {
            if (!candidate || typeof candidate !== 'object') continue;
            if (Array.isArray(candidate.item) || Array.isArray(candidate.requests) || Array.isArray(candidate.order) ||
                Array.isArray(candidate.values) && candidate.values.length > 0 ||
                candidate.url && candidate.method || candidate.collection && candidate.name) return candidate;
        }
    }
    return null;
}
for (const row of input) {
    const bytes = Buffer.from(row, 'base64');
    let found = false;
    for (let offset = 0; offset < Math.min(24, bytes.length); offset++) {
        if (bytes[offset] !== 255) continue;
        try {
            const value = v8.deserialize(bytes.subarray(offset));
            const record = relevant(value);
            if (record) records.push(record);
            found = true;
            break;
        } catch (_) { }
    }
    if (!found) failed++;
}
fs.writeFileSync(process.argv[3], JSON.stringify({ records, failed }, (_, value) =>
    value instanceof Map ? Object.fromEntries(value) : value instanceof Set ? [...value] : value));
