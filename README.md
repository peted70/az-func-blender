# Azure :heart: Blender

In this post I will explore running Blender in an Azure function in order to automate elements of a 3D model pipeline in a scalable and cost-effective way. I will provide a code repo of all of the elements required from the Azure Function code to an example Docker file describing the container that we will run the Azure function in to some example Python scripts which allow automation of Blender functionality.

## Content Pipeline

Just to set the scene I'll give a simple illustrative example of what I mean by a content pipeline and give some examples of what it might be used for. So, imagine that I am starting with some high-resolution 3D models that have been created with some scanning hardware and the model has too much geometry to run efficiently on a mobile device such as HoloLens or a mobile phone. I also have some software that will optimise the model for the target devices but it only takes the glTF file format as input. So if I can create a pipeline node that will convert my input file to and from glTF then I can string nodes together as below to create my content pipeline.

![Content Pipeline](./content-pipeline.drawio.svg)

Blender can very easily be used to automate 3D file format conversions but can also be used to automate more complex scenarios such as:

- Generating synthetic data for input to train Machine Learning models to do object recognition

- Generating normal maps by baking lighting information from a high density 3D model onto a normal map to be used with a model that has reduced geometry so we can preserve a lot of the lighting detail.

- Generating a rendered movie of a camera orbiting around a 3D model so that the result can be checked as part of a model quality checking process

These are a few useful examples that spring to mind as I have real-world experience with these but of course, the combinations are endless and given a set of processing nodes can be configured to fit the scenario.

The rest of this post will be concerned with all aspects of setting up a pipeline like this. The moving parts we will need to understand for this are:

- How do we automate Blender to carry out the processing for nodes we might need?

- How can we host the processing in the cloud? We are going to be using the Azure cloud for this.

## Blender

Blender has been hitting the news a bit recently with some notable additions to the [Blender Development Fund](https://fund.blender.org/)

![Blender Development Fund](./images/blenderdevfund.png)

> Blender has been around for 25 years or so as a free and open source 3D content creation and rendering tool. It has become a cornerstone in the 3D pipeline and it's freeness sets it apart from some of it's competitors. The industry investment is a signal that Blender is a key piece of technology for the future landscape and another indicator that the tipping point for adoption of spatial computing is in the not-too-distant future.  

Blender can be used for 3D modeling, sculpting, creating and applying materials, rendering, character rigging, particle simulation, animation, 2D animation, editing and compositing.

> For Windows 10 users; Blender also appears in the Windows 10 Store so you can make use of auto-updates

### Blender Automation

So, as well as all of the rich functionality for 3D content creation we can also automate it using the Python scripting interface. Blender has an embedded Python interpretor and a Python library exposing most of the functionality. The first step would be to open the Scripting tab which can be found along the top of the application to the far right.

![Blender Scripting Workspace](./images/blender-scripting.png)

This configures Blender into a scripting friendly workspace where you have the following windows:

- 3D viewport

> The usual 3D viewport but allows you to visualise script commands as you run them

- Python console

> You can run commands here and also explore the bpy library. type **bpy.** and then press **TAB** for an autocomplete list

- Info window

> As you carry out user operations in Blender the associated script will get output here. You can copy the output from here directly into your script. (Note. this won't always give you what you want as some of the operations are highly context sensitive but it provides a good starting point)

- Text Editor (where we write the Python code)

> Enter the text for the script here, always starting with **import bpy**. Under the templates menu you can find examples of python scripting from creating add-ons to UI-driven scripts. (We're only interested in **Background Job** for now).

![Python Templates](./images/python-templates.png)

I'm no going to attempt a tutorial of this as there are many online already but I have included some useful tips.

#### Visual Studio Code Extension

One last tip is to point you at the VS Code Extension for Blender Development.

![VS Code Extension](./images/vscode-ext.png)

## Example Content Pipeline Script

I'll now present the example script I'm going to use for this post.

The script is an example of how you can run blender from the command line (in background mode with no interface) to automate tasks. This example will

> - load a .obj file
> - load albedo, normal and ambient occlusion/roughness/metallic maps
> - create a Principled BSDF material using those textures as input
> - output the file as either an obj + obj material (without the PBR textures since those are unsupported in obj format) or a glTF file with the PBR textures assigned.

For now, to run this you would need to have blender installed and it's executable location in your PATH. Then you can run it like this where all args after the -- are passed to the script. File output options are currently gltf and obj.

```bat
blender -b -P ./scripts/objmat.py -- -i ./TestModels/barramundi.obj -o gltf
```

The result should be a copy of the file in a folder named *converted* with *_converted* appended to the filename and with an additional .mtl file in the same folder as the original file. Alternatively, there will be a glTF file there which references the loose textures. The script recursively searches folders from the original file location looking for textures by name, i.e. albedo, normal and orm. There is a distinct lack of error handling as I just wanted to use this as a handy example.

Notes. Some 3D software supports an ORM map in an obj material by adding the line

> map_ORM ORM.png

to the .mtl file. This is non-standard and un-supported by some 3D tools. For this kind of PBR support glTF can be used.

To be honest it doesn't really matter what the script does as long as we can show a script which takes some input and process it to provide some measurable output, and I have this script written and tested already.  

Here's the script being run from the command line and also showing the output:

![Blender script running](./images/blender-script.gif)

> The sample input is provided in the Github repo

Here's the main part of the script:

``` python
def load_obj_and_create_material(input_file, outputFormat):

    # Clear existing objects.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    
    # Clear the current scene - useful if we want to run inside blender and retain preferences
    # for item in bpy.context.scene.objects:
    #    bpy.data.objects.remove(item, do_unlink=True)
    bpy.ops.import_scene.obj(filepath=input_file)
    
    #obj_object = bpy.context.selected_objects[0] ####<--Fix
    # make sure to get all imported objects
    obs = [ o for o in bpy.context.scene.objects if o.select_get() ]

    print('Imported objects: count = ' + str(len(obs)))
    for ob in obs:
        print(ob.name)

    numImportedPolygons = 0
    for ob in obs:
        numImportedPolygons += len(ob.data.polygons)
        
    print('Number of imported polygons = ' + str(numImportedPolygons))

    newmat = bpy.data.materials.new('newmat')
    newmat.use_nodes = True
    node_tree = newmat.node_tree

    # asign the new material to each imported mesh
    for ob in obs:
        # Assign it to object
        if ob.data.materials:
            # assign to 1st material slot
            ob.data.materials[0] = newmat
        else:
            # no slots
            ob.data.materials.append(newmat)

    nodes = node_tree.nodes
    pbdf = nodes.get("Principled BSDF")
    
    albedoFile = ''
    normalFile = ''
    ORMFile = ''

    # we want to locate and load image by name so albedo, normal and ORM
    for dirpath, _, files in os.walk(os.path.dirname(input_file)):
        for filename in files:
            filenameLower = filename.lower()
            if filenameLower.endswith('.png'):
                print(filename)
                if IsAlbedo(filenameLower):
                    albedoFile = os.path.abspath(os.path.join(dirpath, filename))
                elif IsNormal(filenameLower):
                    normalFile = os.path.abspath(os.path.join(dirpath, filename))
                elif IsORM(filenameLower):
                    ORMFile = os.path.abspath(os.path.join(dirpath, filename))

    links = node_tree.links

    if albedoFile:
        # Create albedo node and wire it up
        img = bpy.data.images.load(albedoFile)
        albedoNode = newmat.node_tree.nodes.new(type='ShaderNodeTexImage')
        albedoNode.image = img
        links.new(albedoNode.outputs['Color'], pbdf.inputs['Base Color'])

    if normalFile:
        # Create normal map node and wire it up
        img = bpy.data.images.load(normalFile)
        
        normalImageNode = newmat.node_tree.nodes.new(type='ShaderNodeTexImage')
        normalImageNode.image = img
        normalImageNode.image.colorspace_settings.name = 'Non-Color'

        normalMapNode = newmat.node_tree.nodes.new(type='ShaderNodeNormalMap')
        links.new(normalImageNode.outputs['Color'], normalMapNode.inputs['Color'])
        links.new(normalMapNode.outputs['Normal'], pbdf.inputs['Normal'])
    
    if ORMFile:    
        # Create the ORM mapping and wire it up to the BSDF shader
        img = bpy.data.images.load(ORMFile)
        ormNode = newmat.node_tree.nodes.new(type='ShaderNodeTexImage')
        ormNode.image = img
        
        # pipe the output from the image node into an RGB splitter node
        rgbSplitterNode = newmat.node_tree.nodes.new(type='ShaderNodeSeparateRGB')
        
        links = node_tree.links
        links.new(ormNode.outputs['Color'], rgbSplitterNode.inputs['Image'])
        links.new(rgbSplitterNode.outputs['G'], pbdf.inputs['Roughness'])        
        links.new(rgbSplitterNode.outputs['B'], pbdf.inputs['Metallic'])        
            
    # finally we need to export the obj again and hopefully it will have our material
    dirName = os.path.dirname(input_file)
    dirName = os.path.join(dirName, "converted")
    baseName = os.path.basename(input_file)
    filenameWithoutExt, _ = os.path.splitext(baseName)

    if not os.path.exists(dirName):
        os.makedirs(dirName)

    outputFormat = outputFormat.lower()

    outpathWithoutExt = os.path.join(dirName, filenameWithoutExt + '_converted')

    if outputFormat == 'gltf':
        output_file = outpathWithoutExt + '.gltf'
        print('Output File = ' + output_file)
        bpy.ops.export_scene.gltf(filepath=output_file,export_format='GLTF_SEPARATE',export_image_format='JPEG')
    elif outputFormat == 'obj':
        output_file = outpathWithoutExt + '.obj'
        print('Output File = ' + output_file)
        bpy.ops.export_scene.obj(filepath=output_file)
```

## Running on Azure

So, we have our unit of processing that we can currently run from the command line. (I'm developing this on Windows but this would also run on Linux or Mac).

I decided to set up a Docker container with Blender installed and a selection of python scripts copied over and ready to be called.

This choice, while seemingly simple involved some trade-offs and considerations:

- An Azure function hosted in a custom container requires an App Service Plan and does not run in the usual Consumption Plan providing true pay-as-you-go.

- Using a container approach makes the solution flexible and the service layer can be switched more easily and the containers can be run locally on a development PC or on a local network.

- Azure Container Instances presented another possible solution possibly using a Logic App or Durable Function to coordinate the containers.

Either way, I decided to start first by creating a custom Docker container.



Consumption Plan vs App Service Plan for Functions
Azure Functions vs Azure Container Instances


A little bit about WSL:Ubuntu VS code


<explore cost of running Azure functions - am I using consumption plan or appservice plan and what is the difference.>
