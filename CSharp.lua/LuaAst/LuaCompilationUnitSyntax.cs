/*
Copyright 2017 YANG Huan (sy.yanghuan@gmail.com).

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

  http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CSharpLua.LuaAst {
  public sealed class LuaCompilationUnitSyntax : LuaSyntaxNode {
    public sealed class UsingDeclare {
      public string Prefix;
      public string NewPrefix;
      public bool IsFromCode;
    }

    public string FilePath { get; }
    public readonly LuaSyntaxList<LuaStatementSyntax> Statements = new LuaSyntaxList<LuaStatementSyntax>();
    private LuaStatementListSyntax importAreaStatements = new LuaStatementListSyntax();
    private bool isImportLinq_;
    private int typeDeclarationCount_;
    private List<UsingDeclare> usingDeclares_ = new List<UsingDeclare>();

    public LuaCompilationUnitSyntax(string filePath = "") {
      FilePath = filePath;
      var info = Assembly.GetExecutingAssembly().GetName();
      LuaShortCommentStatement versonStatement = new LuaShortCommentStatement($" Generated by {info.Name} Compiler {GetVersion(info.Version)}");
      AddStatement(versonStatement);

      var system = LuaIdentifierNameSyntax.System;
      AddImport(system, system);
    }

    private static string GetVersion(Version version) {
      return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public void AddStatement(LuaStatementSyntax statement) {
      Statements.Add(statement);
    }

    public bool IsEmpty {
      get {
        return typeDeclarationCount_ == 0;
      }
    }

    public void ImportLinq() {
      if (!isImportLinq_) {
        AddImport(LuaIdentifierNameSyntax.Linq, LuaIdentifierNameSyntax.SystemLinqEnumerable);
        isImportLinq_ = true;
      }
    }

    private void AddImport(LuaIdentifierNameSyntax name, LuaExpressionSyntax value) {
      importAreaStatements.Statements.Add(new LuaLocalVariableDeclaratorSyntax(name, value));
    }

    internal void AddTypeDeclarationCount() {
      ++typeDeclarationCount_;
    }

    internal void AddImport(string prefix, string newPrefix, bool isFromCode) {
      if (!usingDeclares_.Exists(i => i.Prefix == prefix)) {
        usingDeclares_.Add(new UsingDeclare() {
          Prefix = prefix,
          NewPrefix = newPrefix,
          IsFromCode = isFromCode,
        });
      }
    }

    /// <summary>
    /// Sorts and adds all using statements at the start of the file to the Lua file and adds a call to System.usingDeclare to define the variabeles created.
    /// </summary>
    private void CheckUsingDeclares()
    {
      // Sort all using declares
      usingDeclares_.Sort((x, y) =>
      {
        if (x.IsFromCode && !y.IsFromCode)
        {
          return -1;
        }
        return x.Prefix.CompareTo(y.Prefix);
      });

      // Get all secundary assembly using declarations
      var imports = usingDeclares_.Where(i => !i.IsFromCode).ToList();
      if (imports.Count > 0)
      {
        // Insert an local variabele definition for each using statement
        foreach (var import in imports)
        {
          AddImport(new LuaIdentifierNameSyntax(import.NewPrefix), null); // Lua code: 'local ImportName'
        }
      }

      // Get all using statements from assembly
      var usingDeclares = usingDeclares_.Where(i => i.IsFromCode).ToList();
      if (usingDeclares.Count > 0)
      {
        // Insert an local variabele definition for each using statement
        foreach (var usingDeclare in usingDeclares)
        {
          AddImport(new LuaIdentifierNameSyntax(usingDeclare.NewPrefix), null); // Lua code: 'local ImportName'
        }
      }

      // Create the System.usingDeclare anonymous function for this Lua file
      var global = LuaIdentifierNameSyntax.Global;
      LuaFunctionExpressionSyntax functionExpression = new LuaFunctionExpressionSyntax();
      // Add the global parameter to the function we can use for accessing namespaces
      functionExpression.AddParameter(global);

      // Determine how each local variabele we created for each usingDeclare will be defined
      foreach (var usingDeclare in usingDeclares_) // loop trough both imports and other using declares
      {
        LuaAssignmentExpressionSyntax assignment;

        var root = "";
        try
        {
          // Find the string representation of the root namepsace of the usingdeclare
          root = usingDeclare.Prefix.Substring(0, usingDeclare.Prefix.IndexOf('.'));
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        // If the using declare is a root namespace
        if (usingDeclare.Prefix == usingDeclare.NewPrefix
            // or No parent namespace of the current using declare was defined
            || !usingDeclares.Exists(x => x.Prefix == root))
        {
          // Global is needed to find the namespace
          LuaMemberAccessExpressionSyntax right = new LuaMemberAccessExpressionSyntax(global, new LuaIdentifierNameSyntax(usingDeclare.Prefix));
          assignment = new LuaAssignmentExpressionSyntax(new LuaIdentifierNameSyntax(usingDeclare.NewPrefix), right);
        }
        else
        {
          // A usingdeclare of the root namespace of the current usingdeclare was found, so no need to use global
          assignment = new LuaAssignmentExpressionSyntax(new LuaIdentifierNameSyntax(usingDeclare.NewPrefix), new LuaIdentifierNameSyntax(usingDeclare.Prefix));
        }

        // Add the local variabele assignment to the usingDeclare function body
        functionExpression.Body.Statements.Add(new LuaExpressionStatementSyntax(assignment));
      }

      // End the anonymous function we passed to System.usingDeclare and close the parameter bracket
      LuaInvocationExpressionSyntax invocationExpression = new LuaInvocationExpressionSyntax(LuaIdentifierNameSyntax.UsingDeclare, functionExpression);
      importAreaStatements.Statements.Add(new LuaExpressionStatementSyntax(invocationExpression));

      int index = Statements.FindIndex(i => i is LuaNamespaceDeclarationSyntax);
      if (index != -1)
      {
        Statements.Insert(index, importAreaStatements);
      }
    }

    internal override void Render(LuaRenderer renderer) {
      CheckUsingDeclares();
      renderer.Render(this);
    }
  }
}