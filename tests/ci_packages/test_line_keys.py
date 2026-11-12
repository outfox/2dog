"""Godot lines run side by side (ci/<line>/* pushes, later a line axis). Cache and sticky-disk keys outlive a run and
diagnostics get compared across runs, so they always carry the line; other artifacts meet another line only inside a
run with a line axis. Otherwise lines restore and commit each other's state, or one line's dumps pass for the other's."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
SHARED = re.compile(r'uses:\s*(actions/cache|actions/upload-artifact|actions/download-artifact|useblacksmith/stickydisk)\b')
FIELD = re.compile(r'(key|restore-keys|name):\s*(.*)$')
LINE_REF = re.compile(r'\b(?:matrix|inputs|outputs)\.line\b')
DIAGNOSTIC = re.compile(r'\b(?:failure|always)\(\)')
SHARED_OK = '# shared across lines'  # deliberate: the state is the same for every line (e.g. the Android SDK)


def indent(text):
    return len(text) - len(text.lstrip())


def key_column(text):
    return indent(text) + (2 if text.lstrip().startswith('- ') else 0)


def jobs(workflow):
    """The lines before `jobs:`, and each job's lines."""
    lines = workflow.splitlines()
    start = next(i for i, text in enumerate(lines) if text.rstrip() == 'jobs:')
    blocks, current = {}, None
    for text in lines[start + 1:]:
        if match := re.match(r'^  ([\w-]+):\s*$', text):
            current = blocks.setdefault(match.group(1), [])
        elif current is not None:
            current.append(text)
    return lines[:start], blocks


def declares_line(lines, parent):
    """Whether a `parent:` block (a strategy matrix, workflow inputs) declares `line`."""
    for i, text in enumerate(lines):
        if text.strip() == f'{parent}:':
            for inner in lines[i + 1:]:
                if inner.strip() and indent(inner) <= indent(text):
                    break
                if re.match(r'\s+(?:- )?line:', inner):
                    return True
    return False


def steps(body):
    """(action, if-condition, key/restore-keys/name values) of each cache, sticky-disk or artifact step."""
    for i, text in enumerate(body):
        if not (match := SHARED.search(text)):
            continue
        column = text.index('uses:')
        start = next(j for j in range(i, -1, -1) if body[j].lstrip().startswith('- ') and key_column(body[j]) == column)
        end = next((j for j in range(i + 1, len(body)) if body[j].strip() and indent(body[j]) < column), len(body))
        condition, values, field = '', [], None
        for line in body[start:end]:
            content = line.strip().removeprefix('- ')
            if not content:
                continue
            if key_column(line) == column:
                field = None
                if content.startswith('if:'):
                    condition = content[3:]
            elif indent(line) == column + 2:
                found = FIELD.match(content)
                field = found and found.group(1)
                if found and found.group(2).strip() not in ('', '|', '>'):
                    values.append(found.group(2).strip())
            elif field and indent(line) > column + 2:
                values.append(content)
        yield match.group(1), condition, values


def violations(name, workflow):
    header, blocks = jobs(workflow)
    every_job = declares_line(header, 'inputs')  # a workflow called or dispatched for one line
    for job, body in blocks.items():
        per_line = every_job or declares_line(body, 'matrix')
        for action, condition, values in steps(body):
            if (action in ('actions/cache', 'useblacksmith/stickydisk') or per_line or
                    action == 'actions/upload-artifact' and DIAGNOSTIC.search(condition)):
                yield from (f'{name}: {job}: {v}' for v in values if not LINE_REF.search(v) and SHARED_OK not in v)


STEPS = '''
    steps:
      - name: Cache
        uses: actions/cache@v6
        with:
          path: x
          key: build-${{ matrix.precision }}-%s
          restore-keys: |
            build-${{ matrix.precision }}-%s
      - uses: actions/upload-artifact@v7
        with:
          name: packages-${{ matrix.precision }}-%s
      - name: Upload dumps
        if: failure()
        uses: actions/upload-artifact@v7
        with:
          name: dumps-%s
'''
LINE = '${{ needs.check-natives.outputs.line }}'


def workflow(refs, header='', axis=True):
    matrix = '    strategy:\n      matrix:\n        line: [\'4.7\', \'4.8\']\n' if axis else ''
    return f'name: ci\n{header}jobs:\n  pack:\n    outputs:\n      line: x\n{matrix}{STEPS % refs}'


class LineKeyTests(unittest.TestCase):
    def test_workflows_keep_lines_apart(self):
        found = [v for path in sorted((ROOT / '.github/workflows').glob('*.yml'))
                 for v in violations(path.name, path.read_text(encoding='utf-8'))]
        self.assertEqual(found, [], 'add the line (needs.check-natives.outputs.line, matrix.line or inputs.line) to '
                         f'these keys and names, or end the line with "{SHARED_OK}" if every line uses the same state')

    def test_keys_naming_the_line_pass(self):
        self.assertEqual(list(violations('ci.yml', workflow((LINE,) * 4))), [])

    def test_a_line_axis_checks_every_name(self):
        found = list(violations('ci.yml', workflow(('', LINE, '', ''))))
        self.assertEqual(found, ['ci.yml: pack: build-${{ matrix.precision }}-',
                                 'ci.yml: pack: packages-${{ matrix.precision }}-', 'ci.yml: pack: dumps-'])

    def test_restore_key_block_lines_are_checked(self):
        found = list(violations('ci.yml', workflow((LINE, '', LINE, LINE))))
        self.assertEqual(found, ['ci.yml: pack: build-${{ matrix.precision }}-'])

    def test_caches_and_diagnostics_need_the_line_without_an_axis(self):
        # The `line` output is not an axis: the hand-off artifact may stay shared within the run.
        found = list(violations('ci.yml', workflow(('', '', '', ''), axis=False)))
        self.assertEqual(found, ['ci.yml: pack: build-${{ matrix.precision }}-',
                                 'ci.yml: pack: build-${{ matrix.precision }}-', 'ci.yml: pack: dumps-'])

    def test_marked_keys_may_be_shared(self):
        self.assertEqual(list(violations('ci.yml', workflow(('v1 ' + SHARED_OK, LINE, LINE, LINE)))), [])

    def test_a_line_input_covers_every_job(self):
        header = 'on:\n  workflow_call:\n    inputs:\n      line:\n        type: string\n'
        self.assertEqual(len(list(violations('natives.yml', workflow(('', '', '', ''), header, axis=False)))), 4)


if __name__ == '__main__':
    unittest.main()
