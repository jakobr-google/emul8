//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;

namespace Emul8.UserInterface.Tokenizer
{
    public class LiteralToken : Token
    {
        public LiteralToken(string value):base(value)
        {
            Value = value;
        }

        public string Value { get; private set; }

        public override object GetObjectValue()
        {
            return Value;
        }

        public override string ToString()
        {
            return string.Format("[Literal: Value={0}]", Value);
        }
    }



}

