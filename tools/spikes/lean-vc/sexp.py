"""SMT-LIB s-expressions: a tokenizer and a reader of one top-level form at a time."""
import re

TOKEN = re.compile(r'\s+|;[^\n]*|(\(|\)|\|[^|]*\||"(?:[^"]|"")*"|[^\s()|";]+)')


def forms(text):
    """Yields each top-level form as nested lists of str. A quoted symbol keeps its bars."""
    stack = []
    for m in TOKEN.finditer(text):
        tok = m.group(1)
        if tok is None:
            continue
        if tok == '(':
            stack.append([])
        elif tok == ')':
            done = stack.pop()
            if stack:
                stack[-1].append(done)
            else:
                yield done
        elif stack:
            stack[-1].append(tok)
        else:
            yield tok


def name(sym):
    """A symbol without the bars SMT-LIB lets it be quoted in."""
    return sym[1:-1] if sym.startswith('|') else sym
