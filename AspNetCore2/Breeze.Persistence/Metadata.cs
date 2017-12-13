﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Breeze.Persistence {
  public class BreezeMetadata {

    public string MetadataVersion { get; set; }
    public string NamingConvention { get; set; }
    public List<MetaType> StructuralTypes {
      get; set;
    }
  }

  public class MetaType {

    public MetaType() {
      DataProperties = new List<MetaDataProperty>();
      NavigationProperties = new List<MetaNavProperty>();
      
    }

    
    
    public string ShortName { get; set; }
    public string Namespace { get; set; }

    public AutoGeneratedKeyType AutoGeneratedKeyType {
      get; set;
    }

    public string AutoGeneratedKeyTypeName {
      get { return Enum.GetName(typeof(AutoGeneratedKeyType), this.AutoGeneratedKeyType); }
    }

    public string DefaultResourceName {
      get; set;
    }

    public bool IsComplexType { get; set; }

    
    public List<MetaDataProperty> DataProperties {
      get;set;
    }
    public List<MetaNavProperty> NavigationProperties {
      get; set;
    }
  }

  public class MetaProperty {
    public string NameOnServer { get; set; }
    
    

    public List<MetaValidator> Validators {
      get; set;
    }
    
  }

  public class MetaDataProperty : MetaProperty {

    public string DataType { get; set; }
    public bool IsPartOfKey { get; set; }

    public bool IsNullable { get; set; }

    public int? MaxLength { get; set; }

    public Object DefaultValue { get; set; }

    public string ConcurrencyMode { get; set; }

    // Used with 'Undefined' DataType
    public string RawTypeName { get; set; }
  }

  public class MetaNavProperty : MetaProperty {
    public string EntityTypeName { get; set; }
    public bool IsScalar { get; set; }
    public string AssociationName { get; set; }

    public List<String> ForeignKeyNamesOnServer {
      get; set;
    }
    public List<String> InvForeignKeyNamesOnServer {
      get; set;
    }
  }

  public class MetaValidator {
    public string Name {
      get; set;
    }
  }


}
