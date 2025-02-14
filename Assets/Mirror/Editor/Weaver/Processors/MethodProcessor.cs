using Mono.CecilX;
using Mono.CecilX.Cil;

namespace Mirror.Weaver
{
    public static class MethodProcessor
    {
        const string RpcPrefix = "UserCode_";

        // creates a method substitute
        // For example, if we have this:
        //  public void CmdThrust(float thrusting, int spin)
        //  {
        //      xxxxx
        //  }
        //
        //  it will substitute the method and move the code to a new method with a provided name
        //  for example:
        //
        //  public void CmdTrust(float thrusting, int spin)
        //  {
        //  }
        //
        //  public void <newName>(float thrusting, int spin)
        //  {
        //      xxxxx
        //  }
        //
        //  Note that all the calls to the method remain untouched
        //
        //  the original method definition loses all code
        //  this returns the newly created method with all the user provided code
        public static MethodDefinition SubstituteMethod(Logger Log, TypeDefinition td, MethodDefinition md, ref bool WeavingFailed)
        {
            string newName = Weaver.GenerateMethodName(RpcPrefix, md);

            MethodDefinition cmd = new MethodDefinition(newName, md.Attributes, md.ReturnType);

            // force the substitute method to be protected.
            // -> public would show in the Inspector for UnityEvents as
            //    User_CmdUsePotion() etc. but the user shouldn't use those.
            // -> private would not allow inheriting classes to call it, see
            //    OverrideVirtualWithBaseCallsBothVirtualAndBase test.
            // -> IL has no concept of 'protected', it's called IsFamily there.
            cmd.IsPublic = false;
            cmd.IsFamily = true;

            // add parameters
            foreach (ParameterDefinition pd in md.Parameters)
            {
                cmd.Parameters.Add(new ParameterDefinition(pd.Name, ParameterAttributes.None, pd.ParameterType));
            }

            // swap bodies
            (cmd.Body, md.Body) = (md.Body, cmd.Body);

            // Move over all the debugging information
            foreach (SequencePoint sequencePoint in md.DebugInformation.SequencePoints)
                cmd.DebugInformation.SequencePoints.Add(sequencePoint);
            md.DebugInformation.SequencePoints.Clear();

            foreach (CustomDebugInformation customInfo in md.CustomDebugInformations)
                cmd.CustomDebugInformations.Add(customInfo);
            md.CustomDebugInformations.Clear();

            (md.DebugInformation.Scope, cmd.DebugInformation.Scope) = (cmd.DebugInformation.Scope, md.DebugInformation.Scope);

            td.Methods.Add(cmd);

            FixRemoteCallToBaseMethod(Log, td, cmd, ref WeavingFailed);
            return cmd;
        }

        // Finds and fixes call to base methods within remote calls
        //For example, changes `base.CmdDoSomething` to `base.CallCmdDoSomething` within `this.CallCmdDoSomething`
        public static void FixRemoteCallToBaseMethod(Logger Log, TypeDefinition type, MethodDefinition method, ref bool WeavingFailed)
        {
            string callName = method.Name;

            // Cmd/rpc start with Weaver.RpcPrefix
            // e.g. CallCmdDoSomething
            if (!callName.StartsWith(RpcPrefix))
                return;

            // e.g. CmdDoSomething
            string baseRemoteCallName = method.Name.Substring(RpcPrefix.Length);

            foreach (Instruction instruction in method.Body.Instructions)
            {
                // if call to base.CmdDoSomething within this.CallCmdDoSomething
                if (IsCallToMethod(instruction, out MethodDefinition calledMethod) &&
                    calledMethod.Name == baseRemoteCallName)
                {
                    TypeDefinition baseType = type.BaseType.Resolve();
                    MethodDefinition baseMethod = baseType.GetMethodInBaseType(callName);

                    if (baseMethod == null)
                    {
                        Log.Error($"Could not find base method for {callName}", method);
                        WeavingFailed = true;
                        return;
                    }

                    if (!baseMethod.IsVirtual)
                    {
                        Log.Error($"Could not find base method that was virtual {callName}", method);
                        WeavingFailed = true;
                        return;
                    }

                    instruction.Operand = baseMethod;
                }
            }
        }

        static bool IsCallToMethod(Instruction instruction, out MethodDefinition calledMethod)
        {
            if (instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodDefinition method)
            {
                calledMethod = method;
                return true;
            }
            else
            {
                calledMethod = null;
                return false;
            }
        }
    }
}
