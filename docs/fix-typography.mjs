#!/usr/bin/env node
/**
 * Fix typography in markdown files:
 * - Replace em-dashes (—) with en-dashes surrounded by spaces ( – )
 * - Replace smart double quotes ("") with straight quotes (")
 * - Replace smart single quotes ('') with straight quotes (')
 * - Replace ellipsis (…) with three periods (...)
 */

import { readFileSync, writeFileSync, readdirSync, statSync } from 'fs';
import { join, extname } from 'path';

// Unicode escapes on purpose: literal smart characters here would be rewritten
// if this file is ever run through a typography pass, silently neutering the
// patterns (which is exactly what happened once - ASCII quote classes match
// plain apostrophes, "fix" nothing, and report success).
const replacements = [
  { pattern: /\u2014/g, replacement: ' \u2013 ', name: 'em-dashes' },
  { pattern: /[\u201C\u201D]/g, replacement: '"', name: 'smart double quotes' },
  { pattern: /[\u2018\u2019]/g, replacement: "'", name: 'smart single quotes' },
  { pattern: /\u2026/g, replacement: '...', name: 'ellipsis' },
];

function findMarkdownFiles(dir, files = []) {
  const entries = readdirSync(dir);
  
  for (const entry of entries) {
    const fullPath = join(dir, entry);
    
    // Skip node_modules and hidden directories
    if (entry === 'node_modules' || entry.startsWith('.')) {
      continue;
    }
    
    const stat = statSync(fullPath);
    
    if (stat.isDirectory()) {
      findMarkdownFiles(fullPath, files);
    } else if (extname(entry) === '.md') {
      files.push(fullPath);
    }
  }
  
  return files;
}

function fixTypography(filePath) {
  const original = readFileSync(filePath, 'utf8');
  let content = original;
  const fixes = [];

  // Replace-and-compare instead of .test(): a /g regex's .test() advances
  // lastIndex, silently skipping matches on subsequent files.
  for (const { pattern, replacement, name } of replacements) {
    const fixed = content.replace(pattern, replacement);
    if (fixed !== content) {
      content = fixed;
      fixes.push(name);
    }
  }

  if (content !== original) {
    writeFileSync(filePath, content, 'utf8');
    console.log(`Fixed ${fixes.join(', ')} in: ${filePath}`);
    return true;
  }

  return false;
}

// Main execution. Whole-tree scanning is explicit opt-in: the pre-commit hook
// passes staged files, and an accidentally empty list must never rewrite
// every markdown file in the repository.
const args = process.argv.slice(2);
let files;
if (args.includes('--all')) {
  files = findMarkdownFiles('.');
} else if (args.length > 0) {
  files = args;
} else {
  console.error('Usage: fix-typography.mjs <file.md>... | --all');
  process.exit(2);
}

console.log('Fixing typography in markdown files...');

let fixedCount = 0;
for (const file of files) {
  if (fixTypography(file)) {
    fixedCount++;
  }
}

console.log(`Done! Fixed ${fixedCount} file(s).`);
