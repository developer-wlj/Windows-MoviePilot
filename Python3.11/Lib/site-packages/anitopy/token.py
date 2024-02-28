# -*- coding: utf-8 -*-

from __future__ import unicode_literals

from enum import Enum


class TokenCategory(Enum):
    # Auto enumerate elements
    def __new__(cls):
        value = len(cls.__members__) + 1
        obj = object.__new__(cls)
        obj._value_ = value
        return obj

    UNKNOWN = ()
    BRACKET = ()
    DELIMITER = ()
    IDENTIFIER = ()
    INVALID = ()


class TokenFlags:
    NONE = 0
    # Categories
    BRACKET = 1 << 0
    NOT_BRACKET = 1 << 1
    DELIMITER = 1 << 2
    NOT_DELIMITER = 1 << 3
    IDENTIFIER = 1 << 4
    NOT_IDENTIFIER = 1 << 5
    UNKNOWN = 1 << 6
    NOT_UNKNOWN = 1 << 7
    VALID = 1 << 8
    NOT_VALID = 1 << 9
    # Enclosed
    ENCLOSED = 1 << 10
    NOT_ENCLOSED = 1 << 11
    # Masks
    MASK_CATEGORIES = \
        BRACKET | NOT_BRACKET | \
        DELIMITER | NOT_DELIMITER | \
        IDENTIFIER | NOT_IDENTIFIER | \
        UNKNOWN | NOT_UNKNOWN | \
        VALID | NOT_VALID
    MASK_ENCLOSED = ENCLOSED | NOT_ENCLOSED


class Token:
    def __init__(self, category=TokenCategory.UNKNOWN, content=None,
                 enclosed=False):
        self.category = category
        self.content = content
        self.enclosed = enclosed

    def __repr__(self):
        return 'Token(category = {0}, content = "{1}", enclosed = {2}'.format(
            self.category, self.content, self.enclosed
        )

    def check_flags(self, flags):
        def check_flag(flag):
            return (flags & flag) == flag

        if flags & TokenFlags.MASK_ENCLOSED:
            success = self.enclosed if check_flag(TokenFlags.ENCLOSED) \
                else not self.enclosed
            if not success:
                return False

        if flags & TokenFlags.MASK_CATEGORIES:
            def check_category(fe, fn, cat):
                return self.category == cat if check_flag(fe) else \
                       self.category != cat if check_flag(fn) else False
            if check_category(TokenFlags.BRACKET, TokenFlags.NOT_BRACKET,
                              TokenCategory.BRACKET):
                return True
            if check_category(TokenFlags.DELIMITER, TokenFlags.NOT_DELIMITER,
                              TokenCategory.DELIMITER):
                return True
            if check_category(TokenFlags.IDENTIFIER, TokenFlags.NOT_IDENTIFIER,
                              TokenCategory.IDENTIFIER):
                return True
            if check_category(TokenFlags.UNKNOWN, TokenFlags.NOT_UNKNOWN,
                              TokenCategory.UNKNOWN):
                return True
            if check_category(TokenFlags.NOT_VALID, TokenFlags.VALID,
                              TokenCategory.INVALID):
                return True
            return False

        return True


class Tokens:
    INSTANCE = None

    def __init__(self):
        self._tokens = []

    @classmethod
    def instance(cls):
        if not cls.INSTANCE:
            cls.INSTANCE = Tokens()
        return cls.INSTANCE

    @classmethod
    def clear(cls):
        del cls.INSTANCE
        cls.INSTANCE = None

    @classmethod
    def empty(cls):
        return len(cls.instance()._tokens) == 0

    @classmethod
    def append(cls, token):
        cls.instance()._tokens.append(token)

    @classmethod
    def insert(cls, index, token):
        cls.instance()._tokens.insert(index, token)

    @classmethod
    def update(cls, tokens):
        cls.instance()._tokens = tokens

    @classmethod
    def get(cls, index):
        return cls.instance()._tokens[index]

    @classmethod
    def get_list(cls, flags=None, begin=None, end=None):
        tokens = cls.instance()._tokens
        begin_index = 0 if begin is None else cls.get_index(begin)
        end_index = len(tokens) if end is None else cls.get_index(end)
        if flags is None:
            return tokens[begin_index:end_index+1]
        else:
            return [token for token in tokens[begin_index:end_index+1]
                    if token.check_flags(flags)]

    @classmethod
    def get_index(cls, token):
        return cls.instance()._tokens.index(token)

    @classmethod
    def distance(cls, token_begin, token_end):
        begin_index = 0 if token_begin is None else cls.get_index(token_begin)
        end_index = len(cls.instance()._tokens) if token_end is None else \
            cls.get_index(token_end)
        return end_index - begin_index

    @staticmethod
    def _find_in_tokens(tokens, flags):
        for token in tokens:
            if token.check_flags(flags):
                return token
        return None

    @classmethod
    def find(cls, flags):
        return cls._find_in_tokens(cls.instance()._tokens, flags)

    @classmethod
    def find_previous(cls, token, flags):
        tokens = cls.instance()._tokens
        if token is None:
            tokens = tokens[::-1]
        else:
            token_index = cls.get_index(token)
            tokens = tokens[token_index-1::-1]
        return cls._find_in_tokens(tokens, flags)

    @classmethod
    def find_next(cls, token, flags):
        tokens = cls.instance()._tokens
        if token is not None:
            token_index = cls.get_index(token)
            tokens = tokens[token_index+1:]
        return cls._find_in_tokens(tokens, flags)
