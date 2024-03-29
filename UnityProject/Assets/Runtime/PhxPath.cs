using System;
using System.IO;
using UnityEngine;

/// <summary>
/// PhxPath will:<br/>
/// - always use forward slashes<br/>
/// - ensure there is NO trailing slash at the end<br/>
/// - ensure there are no double slashes<br/>
/// - might or might not start with a slash
/// </summary>
public class PhxPath
{
    string P;
    
    public PhxPath(string path)
    {
        P = path;

        // always ensure useage of forward slash
        P = P.Replace('\\', '/');
        P = P.Replace(@"\\", "/");

        // Paths never end with a slash '/'
        if (P.EndsWith("/"))
        {
            P = P.Substring(0, P.Length - 1);
        }
    }

    public static implicit operator string(PhxPath p) => p.P;
    public static implicit operator PhxPath(string p) => new PhxPath(p);

    public static PhxPath operator /(PhxPath lhs, PhxPath rhs) => Concat(lhs, rhs);
    public static PhxPath operator -(PhxPath lhs, PhxPath rhs) => Remove(lhs, rhs);

    public bool Exists() => File.Exists(P) || Directory.Exists(P);

    public override string ToString()
    {
        return P;
    }

    public static PhxPath Concat(PhxPath lhs, PhxPath rhs)
    {
        PhxPath path = new PhxPath(lhs);
        if (!rhs.P.StartsWith("/"))
        {
            path.P += '/';
        }
        path.P += rhs.P;
        return path;
    }

    public static PhxPath Remove(PhxPath lhs, PhxPath rhs)
    {
        PhxPath path = new PhxPath(lhs);
        path.P = path.P.Replace(rhs.P, "");
        if (path.P.StartsWith("/"))
        {
            path.P = path.P.Substring(1, path.P.Length - 1);
        }
        if (path.P.EndsWith("/"))
        {
            path.P = path.P.Substring(0, path.P.Length - 1);
        }
        return path;
    }

    public bool IsFile()
    {
        try
        {
            FileAttributes attr = File.GetAttributes(P);
            return attr != FileAttributes.Directory;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public bool HasExtension(string extension)
    {
        return IsFile() && P.EndsWith(extension, StringComparison.InvariantCultureIgnoreCase);
    }

    // nodeCount: how many nodes to return, starting counting from leaf node
    public PhxPath GetLeaf(int nodeCount)
    {
        Debug.Assert(nodeCount > 0);
        string[] nodes = P.Split('/');
        nodeCount = Mathf.Min(nodeCount, nodes.Length);
        PhxPath result = "";
        for (int i = nodes.Length - nodeCount; i < nodes.Length; ++i)
        {
            result /= nodes[i];
        }
        if (result.P.StartsWith("/"))
        {
            result.P = result.P.Substring(1, result.P.Length - 1);
        }
        return result;
    }

    public PhxPath GetLeaf()
    {
        return GetLeaf(1);
    }

    public bool Contains(PhxPath p)
    {
        return P.IndexOf(p.P, StringComparison.InvariantCultureIgnoreCase) != -1;
    }

    public override int GetHashCode()
    {
        return P.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        PhxPath other = obj as PhxPath;
        if (other == null)
        {
            return false;
        }
        return other.GetHashCode() == GetHashCode();
    }
}