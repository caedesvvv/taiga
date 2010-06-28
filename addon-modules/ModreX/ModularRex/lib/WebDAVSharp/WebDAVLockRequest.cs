﻿using System;
using System.Collections.Generic;
using System.Text;

namespace WebDAVSharp
{
    public class WebDAVLockRequest
    {
        public string Path;
        public LockScope LockScope;
        public LockType LockType;
        public string[] RequestedTimeout;
        public string OwnerNamespaceUri = String.Empty;
        public string OwnerValue = String.Empty;
        public Dictionary<string, string> OwnerValues = new Dictionary<string, string>();
    }
}
