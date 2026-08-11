#!/usr/bin/env python3
"""Verify that the shipped PEM trust anchor matches SafeUpdater's net472 RSA parameters.
Uses only the Python standard library so it can run on clean GitHub-hosted runners.
"""
from __future__ import annotations

import base64
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PEM = ROOT / "update" / "TRUSTED_UPDATE_PUBLIC_KEY.pem"
CS = ROOT / "HealthAutoArrange.Plugin" / "SafeUpdater.cs"


def fail(message: str) -> None:
    print(f"[FAIL] {message}")
    raise SystemExit(1)


def read_length(data: bytes, pos: int) -> tuple[int, int]:
    if pos >= len(data):
        raise ValueError("truncated DER length")
    first = data[pos]
    pos += 1
    if first < 0x80:
        return first, pos
    count = first & 0x7F
    if count == 0 or count > 4 or pos + count > len(data):
        raise ValueError("unsupported DER length")
    value = int.from_bytes(data[pos:pos + count], "big")
    return value, pos + count


def read_tlv(data: bytes, pos: int, expected_tag: int | None = None) -> tuple[int, bytes, int]:
    if pos >= len(data):
        raise ValueError("truncated DER tag")
    tag = data[pos]
    pos += 1
    length, pos = read_length(data, pos)
    end = pos + length
    if end > len(data):
        raise ValueError("truncated DER value")
    if expected_tag is not None and tag != expected_tag:
        raise ValueError(f"unexpected DER tag 0x{tag:02x}, expected 0x{expected_tag:02x}")
    return tag, data[pos:end], end


def parse_spki_rsa_public_key(der: bytes) -> tuple[bytes, bytes]:
    _, outer, end = read_tlv(der, 0, 0x30)
    if end != len(der):
        raise ValueError("trailing DER after SPKI")
    _, _algorithm, pos = read_tlv(outer, 0, 0x30)
    _, bit_string, pos = read_tlv(outer, pos, 0x03)
    if pos != len(outer) or not bit_string or bit_string[0] != 0:
        raise ValueError("invalid SPKI bit string")
    rsa_der = bit_string[1:]
    _, rsa_seq, rsa_end = read_tlv(rsa_der, 0, 0x30)
    if rsa_end != len(rsa_der):
        raise ValueError("trailing DER after RSA key")
    _, modulus_raw, rpos = read_tlv(rsa_seq, 0, 0x02)
    _, exponent_raw, rpos = read_tlv(rsa_seq, rpos, 0x02)
    if rpos != len(rsa_seq):
        raise ValueError("trailing RSA parameters")
    modulus = modulus_raw.lstrip(b"\x00") or b"\x00"
    exponent = exponent_raw.lstrip(b"\x00") or b"\x00"
    return modulus, exponent


def main() -> int:
    try:
        pem_text = PEM.read_text(encoding="ascii")
        body = "".join(
            line.strip() for line in pem_text.splitlines()
            if line and not line.startswith("-----")
        )
        modulus, exponent = parse_spki_rsa_public_key(base64.b64decode(body, validate=True))
        cs = CS.read_text(encoding="utf-8")
        mod_match = re.search(r'PublicModulusBase64\s*=\s*"([A-Za-z0-9+/=]+)"', cs)
        exp_match = re.search(r'PublicExponentBase64\s*=\s*"([A-Za-z0-9+/=]+)"', cs)
        if not mod_match or not exp_match:
            fail("SafeUpdater RSA constants were not found")
        cs_modulus = base64.b64decode(mod_match.group(1), validate=True)
        cs_exponent = base64.b64decode(exp_match.group(1), validate=True)
        if modulus != cs_modulus:
            fail("TRUSTED_UPDATE_PUBLIC_KEY.pem modulus does not match SafeUpdater")
        if exponent != cs_exponent:
            fail("TRUSTED_UPDATE_PUBLIC_KEY.pem exponent does not match SafeUpdater")
        print("[PASS] updater PEM trust anchor matches SafeUpdater RSA parameters")
        print(f"RSA bits={len(modulus) * 8} exponent={int.from_bytes(exponent, 'big')}")
        return 0
    except SystemExit:
        raise
    except Exception as exc:
        fail(f"could not validate updater trust key: {exc}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
